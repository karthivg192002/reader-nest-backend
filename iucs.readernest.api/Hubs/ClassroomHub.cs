using System.Collections.Concurrent;
using System.Security.Claims;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace iucs.readernest.api.Hubs
{
    /// <summary>
    /// Real-time layer of the live classroom, running alongside the Jitsi call:
    /// participant roster, shared whiteboard ops, quiz launch/answers with a live
    /// leaderboard, celebrations and teacher controls. State is per-session and
    /// in-memory — a classroom is ephemeral; nothing here needs to survive a restart
    /// (persistent engagement/awards flow through the REST API instead).
    /// </summary>
    [Authorize]
    public class ClassroomHub : Hub
    {
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ParticipantState>> Rooms = new();
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, int>> Scores = new();
        // Keyed "{sessionId}:{connectionId}:{questionIndex}" — see AnswerQuiz for why this exists.
        private static readonly ConcurrentDictionary<string, byte> AnsweredQuestions = new();

        private readonly ISessionService _sessionService;
        private readonly IGamificationService _gamificationService;
        private readonly IAcademicOpsService _academicOpsService;
        private readonly IClassroomPresenceTracker _presenceTracker;

        public ClassroomHub(
            ISessionService sessionService,
            IGamificationService gamificationService,
            IAcademicOpsService academicOpsService,
            IClassroomPresenceTracker presenceTracker)
        {
            _sessionService = sessionService;
            _gamificationService = gamificationService;
            _academicOpsService = academicOpsService;
            _presenceTracker = presenceTracker;
        }

        public record ParticipantState(string Name, string Role, bool HandRaised);

        private static string Group(string sessionId) => $"classroom-{sessionId}";

        private bool IsTeacher =>
            Context.User?.IsInRole(nameof(UserRole.Teacher)) == true
            || Context.User?.IsInRole(nameof(UserRole.Admin)) == true;

        private string UserName =>
            Context.User?.FindFirstValue(ClaimTypes.Name)
            ?? Context.User?.FindFirstValue("name")
            ?? "Participant";

        /// <summary>
        /// The connection must have already completed a successfully-authorized
        /// JoinSession for THIS exact sessionId — every other hub method is gated on
        /// this so a connection can never act on a room it never legitimately joined
        /// (SendBoard/SendChat/AnswerQuiz would otherwise be a blind relay callable by
        /// anyone who merely knows the session id, without ever having joined it).
        /// </summary>
        private bool IsJoined(string sessionId) =>
            Context.Items.TryGetValue("sessionId", out var value) && value is string joined && joined == sessionId;

        /// <summary>Joined AND the room recorded this connection's role as teacher at join time.</summary>
        private bool IsTeacherInRoom(string sessionId) =>
            IsJoined(sessionId)
            && Rooms.TryGetValue(sessionId, out var room)
            && room.TryGetValue(Context.ConnectionId, out var state)
            && state.Role == "teacher";

        // ---- lifecycle ----

        /// <summary>
        /// Join is the single authorization checkpoint for the whole room: it confirms
        /// the caller genuinely belongs to this session (Admin, the specifically
        /// assigned teacher, or a parent with a child enrolled in the session's batch)
        /// via ISessionService.IsSessionParticipantAsync — the same check the REST
        /// engagement endpoint uses. Without this, any authenticated user of any role
        /// could join, watch, and control a class that isn't theirs by guessing/knowing
        /// its session id.
        /// </summary>
        public async Task JoinSession(string sessionId, string displayName)
        {
            if (!Guid.TryParse(sessionId, out var sessionGuid))
            {
                throw new HubException("Invalid session id.");
            }

            var userIdClaim = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!Guid.TryParse(userIdClaim, out var userId))
            {
                throw new HubException("Not signed in.");
            }

            if (!await _sessionService.IsSessionParticipantAsync(sessionGuid, userId, Context.ConnectionAborted))
            {
                throw new HubException("You do not have access to this session.");
            }

            var name = string.IsNullOrWhiteSpace(displayName) ? UserName : displayName.Trim();
            var role = IsTeacher ? "teacher" : "student";

            // A room that goes fully empty (everyone disconnects, even momentarily) has its
            // in-memory Scores wiped in RemoveFromSessionAsync below — reseed from the durable
            // leaderboard on the FIRST join of a fresh room so a rejoin never shows the class's
            // already-earned stars resetting to zero (StudentAward rows are untouched either way;
            // only this ephemeral cache was ever at risk of looking wrong).
            var isNewRoom = !Rooms.ContainsKey(sessionId);
            var room = Rooms.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, ParticipantState>());
            room[Context.ConnectionId] = new ParticipantState(name, role, HandRaised: false);
            Context.Items["sessionId"] = sessionId;

            if (isNewRoom)
            {
                var persisted = await _gamificationService.GetLeaderboardAsync(sessionGuid, top: 50, Context.ConnectionAborted);
                if (persisted.Count > 0)
                {
                    var scores = Scores.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, int>());
                    foreach (var entry in persisted)
                    {
                        scores[entry.ParticipantName] = entry.Stars;
                    }
                }
            }

            await Groups.AddToGroupAsync(Context.ConnectionId, Group(sessionId));
            _presenceTracker.UserJoined(sessionId, Context.ConnectionId);
            await BroadcastRosterAsync(sessionId);
            await SendLeaderboardAsync(sessionId);

            // PDF's "System Marks Attendance" — join-based capture, fired now that the caller
            // is confirmed to genuinely belong to this session. Best-effort by design (see the
            // method's own doc comment); never allowed to affect the join that already succeeded.
            await _academicOpsService.CaptureJoinAttendanceAsync(sessionGuid, userId, Context.ConnectionAborted);
        }

        public async Task LeaveSession(string sessionId)
        {
            await RemoveFromSessionAsync(sessionId);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (Context.Items.TryGetValue("sessionId", out var value) && value is string sessionId)
            {
                await RemoveFromSessionAsync(sessionId);
            }

            await base.OnDisconnectedAsync(exception);
        }

        // ---- shared whiteboard ----

        /// <summary>Relays one board operation (stroke/clear/page op) to everyone else in the class.</summary>
        public async Task SendBoard(string sessionId, string opJson)
        {
            if (!IsJoined(sessionId))
            {
                return;
            }

            await Clients.OthersInGroup(Group(sessionId)).SendAsync("Board", opJson);
        }

        // ---- chat (interactive panel) ----

        public async Task SendChat(string sessionId, string text)
        {
            if (!IsJoined(sessionId) || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            await Clients.Group(Group(sessionId)).SendAsync("Chat", UserNameFor(sessionId), text.Trim());
        }

        // ---- quiz + leaderboard ----

        /// <summary>Teacher pushes a question to the class by index into the shared bank.</summary>
        public async Task StartQuiz(string sessionId, int questionIndex)
        {
            if (!IsTeacherInRoom(sessionId))
            {
                return;
            }

            await Clients.Group(Group(sessionId)).SendAsync("QuizStarted", questionIndex);
        }

        public async Task EndQuiz(string sessionId)
        {
            if (!IsTeacherInRoom(sessionId))
            {
                return;
            }

            await Clients.Group(Group(sessionId)).SendAsync("QuizEnded");
        }

        /// <summary>
        /// Student answer: correct answers score a star; the leaderboard broadcasts live.
        /// `correct` is reported by the client and isn't independently verifiable here — the
        /// quiz question bank lives entirely in the frontend, so the server has no way to look
        /// up the actual right answer for a given questionIndex. That means a single dishonest
        /// claim per question can't be ruled out architecturally, but this method used to be
        /// callable directly (bypassing the UI's own one-answer-per-question guard) any number
        /// of times for the same question, letting one participant repeatedly credit themselves
        /// and inflate the whole class's live leaderboard. The dedup below caps the exposure at
        /// one claim per (connection, question) — matching what the UI already enforces — rather
        /// than leaving it fully open to a direct hub invoke.
        /// </summary>
        public async Task AnswerQuiz(string sessionId, int questionIndex, int selectedIndex, bool correct)
        {
            if (!IsJoined(sessionId))
            {
                return;
            }

            if (!AnsweredQuestions.TryAdd($"{sessionId}:{Context.ConnectionId}:{questionIndex}", 0))
            {
                return;
            }

            var name = UserNameFor(sessionId);
            if (correct)
            {
                AddLiveStar(sessionId, name);
            }

            await Clients.Group(Group(sessionId)).SendAsync("QuizAnswer", name, questionIndex, selectedIndex, correct);
            await SendLeaderboardAsync(sessionId);
        }

        /// <summary>
        /// Live-leaderboard bump for completing a whiteboard mini-game (drag & drop / tag &
        /// match / hotspot) — these activities only fire their completion callback once every
        /// item is correctly placed, so "completed" already means "correct," same as a right
        /// quiz answer. This only keeps everyone's in-room leaderboard view in sync instantly;
        /// the durable record (StudentAward row, milestone auto-grant) is the client's separate
        /// REST call to POST /api/gamification/awards, exactly like the quiz's own star flow.
        /// </summary>
        public async Task AwardStar(string sessionId)
        {
            if (!IsJoined(sessionId))
            {
                return;
            }

            AddLiveStar(sessionId, UserNameFor(sessionId));
            await SendLeaderboardAsync(sessionId);
        }

        /// <summary>
        /// Teacher-initiated star for a named student — the manual counterpart to the
        /// self-attributed <see cref="AwardStar"/> above (which only ever credits the caller's
        /// own name), for rewarding participation that doesn't happen to run through the quiz
        /// or a whiteboard mini-game. Teacher-only: this is the same trust boundary
        /// GamificationService.GrantAsync already draws server-side (a Parent may only
        /// self-report their own child's Star; a Teacher/Admin may name anyone in the class),
        /// mirrored here since this call only touches the ephemeral live leaderboard — the
        /// durable award is the client's separate POST /api/gamification/awards, which
        /// re-checks that same rule independently.
        /// </summary>
        public async Task AwardStarToParticipant(string sessionId, string participantName)
        {
            if (!IsTeacherInRoom(sessionId) || string.IsNullOrWhiteSpace(participantName))
            {
                return;
            }

            AddLiveStar(sessionId, participantName.Trim());
            await SendLeaderboardAsync(sessionId);
        }

        // ---- celebrations + teacher controls ----

        public async Task Celebrate(string sessionId, string? message)
        {
            if (!IsTeacherInRoom(sessionId))
            {
                return;
            }

            await Clients.Group(Group(sessionId)).SendAsync("Celebrate", message);
        }

        public async Task RaiseHand(string sessionId, bool raised)
        {
            if (IsJoined(sessionId)
                && Rooms.TryGetValue(sessionId, out var room)
                && room.TryGetValue(Context.ConnectionId, out var state))
            {
                room[Context.ConnectionId] = state with { HandRaised = raised };
                await BroadcastRosterAsync(sessionId);
            }
        }

        /// <summary>Teacher-only board permission toggle for a participant (by connection id).</summary>
        public async Task SetBoardAccess(string sessionId, string connectionId, bool allowed)
        {
            if (!IsTeacherInRoom(sessionId))
            {
                return;
            }

            // The target connection must be a member of THIS room too — otherwise a
            // legitimate teacher of one class could grant/revoke board access on an
            // arbitrary connection id belonging to a completely different session.
            if (!Rooms.TryGetValue(sessionId, out var room) || !room.ContainsKey(connectionId))
            {
                return;
            }

            await Clients.Client(connectionId).SendAsync("BoardAccess", allowed);
        }

        // ---- helpers ----

        private async Task RemoveFromSessionAsync(string sessionId)
        {
            _presenceTracker.UserLeft(sessionId, Context.ConnectionId);

            if (Rooms.TryGetValue(sessionId, out var room))
            {
                room.TryRemove(Context.ConnectionId, out var removedState);

                // Real departure time, for payout accuracy (see CaptureLeaveAttendanceAsync's own
                // doc comment) — without this, nothing ever recorded when a teacher actually left
                // a live class, so one who taught the whole thing and one who left after a few
                // minutes were indistinguishable. Only worth the lookup for the departing
                // participant's own role, not every student/parent leaving too.
                if (removedState?.Role == "teacher"
                    && Guid.TryParse(sessionId, out var sessionGuid)
                    && Guid.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                {
                    // CancellationToken.None, deliberately: this is exactly the abrupt-disconnect
                    // (network drop) case that matters most to capture, and Context.ConnectionAborted
                    // may already be signalled by the time OnDisconnectedAsync runs.
                    await _academicOpsService.CaptureLeaveAttendanceAsync(sessionGuid, userId, CancellationToken.None);
                }

                if (room.IsEmpty)
                {
                    Rooms.TryRemove(sessionId, out _);
                    Scores.TryRemove(sessionId, out _); // class over — scoreboard resets
                    foreach (var key in AnsweredQuestions.Keys.Where(k => k.StartsWith($"{sessionId}:", StringComparison.Ordinal)))
                    {
                        AnsweredQuestions.TryRemove(key, out _);
                    }
                }
                else
                {
                    await BroadcastRosterAsync(sessionId);
                }
            }

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(sessionId));
        }

        /// <summary>Shared by AnswerQuiz and AwardStar — bumps the in-memory live score only.</summary>
        private static void AddLiveStar(string sessionId, string name)
        {
            var scores = Scores.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, int>());
            scores.AddOrUpdate(name, 1, (_, current) => current + 1);
        }

        private string UserNameFor(string sessionId) =>
            Rooms.TryGetValue(sessionId, out var room) && room.TryGetValue(Context.ConnectionId, out var state)
                ? state.Name
                : UserName;

        private async Task BroadcastRosterAsync(string sessionId)
        {
            if (!Rooms.TryGetValue(sessionId, out var room))
            {
                return;
            }

            var roster = room
                .Select(kv => new { connectionId = kv.Key, name = kv.Value.Name, role = kv.Value.Role, handRaised = kv.Value.HandRaised })
                .OrderByDescending(p => p.role == "teacher")
                .ThenBy(p => p.name)
                .ToList();
            await Clients.Group(Group(sessionId)).SendAsync("Roster", roster);
        }

        private async Task SendLeaderboardAsync(string sessionId)
        {
            var board = Scores.TryGetValue(sessionId, out var scores)
                ? scores.Select(kv => new { name = kv.Key, stars = kv.Value })
                    .OrderByDescending(e => e.stars)
                    .Take(10)
                    .ToList()
                : [];
            await Clients.Group(Group(sessionId)).SendAsync("Leaderboard", board);
        }
    }
}
