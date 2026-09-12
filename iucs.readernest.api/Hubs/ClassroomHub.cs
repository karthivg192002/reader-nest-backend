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
        // Non-teacher connections SetBoardAccess has granted, per session. SendBoard enforces
        // this server-side -- without it, SetBoardAccess was only ever a one-way "you're now
        // allowed" notification the client could act on or ignore; nothing stopped any joined
        // participant from calling SendBoard directly (visible and callable from the browser's
        // own dev tools) regardless of whether they'd actually been granted access.
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> BoardAccessGrants = new();
        // Every board op sent so far this session, replayed (in order) to a connection that
        // joins after some were already drawn. SendBoard only ever relayed live to whoever was
        // ALREADY connected — a student who joined the call after the teacher started drawing
        // saw a blank board for the rest of the class, while a student who joined earlier kept
        // seeing everything correctly. Confirmed live: two students in the same class, one
        // could see the whiteboard and the other couldn't. Each list is mutated under its own
        // lock — ConcurrentDictionary makes GetOrAdd/TryRemove on the outer map safe, but a
        // plain List<T> itself isn't safe against two students' SendBoard calls landing at once.
        private static readonly ConcurrentDictionary<string, List<string>> BoardHistory = new();
        // Current 0-based page of whatever PDF deck the teacher uploaded and is presenting live
        // (the "like Google Meet" present-a-deck flow) — not the deck file itself (that's the
        // REST-uploaded SessionPresentation, fetched once by URL), just where the teacher's own
        // Next/Previous clicks have gotten to, so JoinSession can land a mid-class joiner on the
        // right slide instead of always page 0.
        private static readonly ConcurrentDictionary<string, int> CurrentSlide = new();

        // Whichever participant the teacher currently has spotlighted/pinned in their own
        // Jitsi view (null = nobody pinned) — see SetPinned's own doc comment for why this
        // needs to exist at all. Persisted the same way CurrentSlide is, so a student who
        // joins mid-class (or reconnects) lands with the teacher's view already applied
        // instead of only picking it up on the next pin change.
        private static readonly ConcurrentDictionary<string, string?> PinnedParticipant = new();

        private readonly ISessionService _sessionService;
        private readonly IGamificationService _gamificationService;
        private readonly IAcademicOpsService _academicOpsService;
        private readonly IClassroomPresenceTracker _presenceTracker;
        private readonly IClassSessionEventLogService _eventLog;

        public ClassroomHub(
            ISessionService sessionService,
            IGamificationService gamificationService,
            IAcademicOpsService academicOpsService,
            IClassroomPresenceTracker presenceTracker,
            IClassSessionEventLogService eventLog)
        {
            _sessionService = sessionService;
            _gamificationService = gamificationService;
            _academicOpsService = academicOpsService;
            _presenceTracker = presenceTracker;
            _eventLog = eventLog;
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

            // Jibri's headless "recording observer" page (see docs/JITSI_ARCHITECTURE.md) has no
            // real logged-in user, so it can't pass IsSessionParticipantAsync -- it authenticates
            // instead with a CreateRecordingObserverHubToken carrying a "purpose" +
            // "sessionId" claim, checked here in place of (never in addition to) the normal
            // userId/participant check. The token's own sessionId claim, not just its mere
            // presence, must match the room being joined -- otherwise one observer token could
            // be replayed to silently watch a different class's whiteboard/quiz traffic.
            var isRecordingObserver = Context.User?.FindFirstValue("purpose") == "recording-observer";
            Guid userId;
            if (isRecordingObserver)
            {
                var tokenSessionId = Context.User?.FindFirstValue("sessionId");
                if (tokenSessionId != sessionId)
                {
                    throw new HubException("Token is not scoped to this session.");
                }
                userId = Guid.Parse(Context.User!.FindFirstValue(ClaimTypes.NameIdentifier)!);
            }
            else
            {
                var userIdClaim = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!Guid.TryParse(userIdClaim, out userId))
                {
                    throw new HubException("Not signed in.");
                }

                if (!await _sessionService.IsSessionParticipantAsync(sessionGuid, userId, Context.ConnectionAborted))
                {
                    await _eventLog.LogJoinDeniedAsync(sessionGuid, userId, "Not a participant of this session.", CancellationToken.None);
                    throw new HubException("You do not have access to this session.");
                }
            }

            var name = isRecordingObserver ? "Recording" : (string.IsNullOrWhiteSpace(displayName) ? UserName : displayName.Trim());
            var role = isRecordingObserver ? "observer" : (IsTeacher ? "teacher" : "student");

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
            _presenceTracker.UserJoined(sessionId, Context.ConnectionId, userId, name, role);
            await BroadcastRosterAsync(sessionId);
            await SendLeaderboardAsync(sessionId);

            // Catch this connection up on whatever's already been drawn — see BoardHistory's
            // own doc comment. Sent one op at a time through the same "Board" event a live op
            // arrives on (SignalR preserves per-connection delivery order), so the client-side
            // handler that already knows how to apply a Board op needs no separate code path
            // for a replayed one.
            if (BoardHistory.TryGetValue(sessionId, out var boardHistory))
            {
                string[] snapshot;
                lock (boardHistory)
                {
                    snapshot = boardHistory.ToArray();
                }
                foreach (var op in snapshot)
                {
                    await Clients.Caller.SendAsync("Board", op);
                }
            }

            // Same idea as the whiteboard replay above, for whichever slide the teacher's
            // already on — a mid-class joiner should see the current slide, not page 0.
            if (CurrentSlide.TryGetValue(sessionId, out var pageIndex))
            {
                await Clients.Caller.SendAsync("Slide", pageIndex);
            }

            // Same idea again for whoever the teacher currently has pinned/spotlighted — a
            // student who joins after the teacher already pinned themselves (or anyone else)
            // should land on that same view, not the room's default tile layout with no pin.
            if (PinnedParticipant.TryGetValue(sessionId, out var pinnedId))
            {
                await Clients.Caller.SendAsync("Pinned", pinnedId);
            }

            // PDF's "System Marks Attendance" — join-based capture, fired now that the caller
            // is confirmed to genuinely belong to this session. Best-effort by design (see the
            // method's own doc comment); never allowed to affect the join that already succeeded.
            // Skipped for the recording observer -- its userId is a throwaway synthetic id with
            // no real attendance to record.
            if (!isRecordingObserver)
            {
                await _academicOpsService.CaptureJoinAttendanceAsync(sessionGuid, userId, Context.ConnectionAborted);
            }
        }

        public async Task LeaveSession(string sessionId)
        {
            // Every deliberate exit — "End the class", "Just leave for now", or a plain
            // Leave — invokes this before the connection actually stops (see
            // ClassroomHubClient.disconnect() in lib/classroomHub.ts), so a call landing
            // here reliably means the participant chose to leave. OnDisconnectedAsync
            // firing WITHOUT this flag having been set first is what actually means an
            // abrupt drop (network loss, browser crash/close).
            await RemoveFromSessionAsync(sessionId, wasExplicit: true);
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            if (Context.Items.TryGetValue("sessionId", out var value) && value is string sessionId)
            {
                // Already removed (and logged) by an explicit LeaveSession moments earlier —
                // Rooms.TryGetValue below will simply find nothing for this connection id.
                await RemoveFromSessionAsync(sessionId, wasExplicit: false);
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

            // The teacher always has access; anyone else needs an explicit grant recorded by
            // SetBoardAccess below — this is the actual enforcement, not just a UI hint.
            var hasAccess = IsTeacherInRoom(sessionId)
                || (BoardAccessGrants.TryGetValue(sessionId, out var grants) && grants.ContainsKey(Context.ConnectionId));
            if (!hasAccess)
            {
                return;
            }

            var history = BoardHistory.GetOrAdd(sessionId, _ => new List<string>());
            lock (history)
            {
                history.Add(opJson);
            }

            await Clients.OthersInGroup(Group(sessionId)).SendAsync("Board", opJson);
        }

        // ---- live annotation overlay (marking on top of the screen share) ----

        /// <summary>Relays one annotation stroke/clear op drawn over the video stage, live only —
        /// teacher-only (unlike the whiteboard, no student-grant path) and deliberately not kept
        /// in any history: it's meant for pointing things out while a child reads on a shared
        /// screen, not a durable record, so a student joining mid-class simply sees nothing until
        /// the teacher draws again.</summary>
        public async Task SendAnnotation(string sessionId, string opJson)
        {
            if (!IsTeacherInRoom(sessionId))
            {
                return;
            }

            await Clients.OthersInGroup(Group(sessionId)).SendAsync("Annotation", opJson);
        }

        // ---- live presentation (present a deck, like Google Meet) ----

        /// <summary>Teacher-only: broadcasts a Next/Previous slide change to the rest of the class
        /// and remembers it so a student who joins afterward lands on the right page — see
        /// CurrentSlide's own doc comment. The deck itself isn't sent here at all; every viewer
        /// already fetched the same PDF by URL once and renders locally.</summary>
        public async Task SendSlide(string sessionId, int pageIndex)
        {
            if (!IsTeacherInRoom(sessionId) || pageIndex < 0)
            {
                return;
            }

            CurrentSlide[sessionId] = pageIndex;
            await Clients.OthersInGroup(Group(sessionId)).SendAsync("Slide", pageIndex);
        }

        // ---- pin / spotlight sync ----

        /// <summary>
        /// Teacher-only: broadcasts whichever participant the teacher just pinned in their own
        /// Jitsi view (or null when they unpin) so every student's view follows along. Jitsi's
        /// native pin is purely local to whichever browser clicked it — a teacher pinning
        /// herself while screen-sharing showed "Pinned" on her own screen only, with students
        /// still seeing her in the small tile, since nothing relayed that choice to anyone
        /// else. `participantId` is the Jitsi endpoint id (the same id `videoConferenceJoined`
        /// hands the pinning participant for themselves), which is the same id every other
        /// participant in the room already knows them by, so the student side can hand it
        /// straight to its own `pinParticipant` command with no lookup needed. A student
        /// pinning someone locally for their own view is left alone — only the teacher's own
        /// pin choice is ever synced, since that's the one everyone is meant to follow.
        /// </summary>
        public async Task SetPinned(string sessionId, string? participantId)
        {
            if (!IsTeacherInRoom(sessionId))
            {
                return;
            }

            PinnedParticipant[sessionId] = participantId;
            await Clients.OthersInGroup(Group(sessionId)).SendAsync("Pinned", participantId);
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

            var grants = BoardAccessGrants.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, byte>());
            if (allowed)
            {
                grants[connectionId] = 0;
            }
            else
            {
                grants.TryRemove(connectionId, out _);
            }

            await Clients.Client(connectionId).SendAsync("BoardAccess", allowed);
        }

        // ---- helpers ----

        private async Task RemoveFromSessionAsync(string sessionId, bool wasExplicit)
        {
            _presenceTracker.UserLeft(sessionId, Context.ConnectionId);

            if (Rooms.TryGetValue(sessionId, out var room))
            {
                room.TryRemove(Context.ConnectionId, out var removedState);
                if (BoardAccessGrants.TryGetValue(sessionId, out var grants))
                {
                    grants.TryRemove(Context.ConnectionId, out _);
                }

                // removedState is null on the SECOND call for the same connection (an explicit
                // LeaveSession already removed it from Rooms moments earlier; the connection then
                // formally closing fires OnDisconnectedAsync too) — nothing left to attribute a
                // departure to, so both the attendance capture and the event log below are
                // naturally skipped rather than double-logging one exit as two. Also excludes the
                // recording observer (see JoinSession) -- its userId is a throwaway synthetic id
                // with no real attendance or leave event to record.
                if (removedState is not null
                    && removedState.Role != "observer"
                    && Guid.TryParse(sessionId, out var sessionGuid)
                    && Guid.TryParse(Context.User?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
                {
                    // Real departure time, for payout accuracy (see CaptureLeaveAttendanceAsync's
                    // own doc comment) — teacher-only, since that's the side payout accuracy
                    // depends on. CancellationToken.None, deliberately: this covers the
                    // abrupt-disconnect (network drop) case too, and Context.ConnectionAborted
                    // may already be signalled by the time OnDisconnectedAsync runs.
                    if (removedState.Role == "teacher")
                    {
                        await _academicOpsService.CaptureLeaveAttendanceAsync(sessionGuid, userId, CancellationToken.None);
                    }

                    // Durable event-log row for BOTH roles — see IClassSessionEventLogService.
                    // Best-effort; never allowed to affect the leave itself.
                    var participantType = removedState.Role == "teacher" ? ParticipantType.Teacher : ParticipantType.Student;
                    await _eventLog.LogLeaveAsync(
                        sessionGuid, participantType,
                        teacherProfileId: null, childId: null, userId: userId,
                        participantName: removedState.Name, wasExplicit: wasExplicit,
                        cancellationToken: CancellationToken.None);
                }

                if (room.IsEmpty)
                {
                    Rooms.TryRemove(sessionId, out _);
                    Scores.TryRemove(sessionId, out _); // class over — scoreboard resets
                    BoardAccessGrants.TryRemove(sessionId, out _);
                    BoardHistory.TryRemove(sessionId, out _);
                    CurrentSlide.TryRemove(sessionId, out _);
                    PinnedParticipant.TryRemove(sessionId, out _);
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

            // Jibri's own "observer" connection (see JoinSession) never appears in the roster
            // real participants see -- it's a robot, not a classmate, and the frontend's roster
            // types/UI have no concept of a third role to render it correctly anyway.
            var roster = room
                .Where(kv => kv.Value.Role != "observer")
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
