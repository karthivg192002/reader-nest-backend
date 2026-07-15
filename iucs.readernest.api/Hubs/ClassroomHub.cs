using System.Collections.Concurrent;
using System.Security.Claims;
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

        public record ParticipantState(string Name, string Role, bool HandRaised);

        private static string Group(string sessionId) => $"classroom-{sessionId}";

        private bool IsTeacher =>
            Context.User?.IsInRole(nameof(UserRole.Teacher)) == true
            || Context.User?.IsInRole(nameof(UserRole.Admin)) == true;

        private string UserName =>
            Context.User?.FindFirstValue(ClaimTypes.Name)
            ?? Context.User?.FindFirstValue("name")
            ?? "Participant";

        // ---- lifecycle ----

        public async Task JoinSession(string sessionId, string displayName)
        {
            var name = string.IsNullOrWhiteSpace(displayName) ? UserName : displayName.Trim();
            var role = IsTeacher ? "teacher" : "student";

            var room = Rooms.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, ParticipantState>());
            room[Context.ConnectionId] = new ParticipantState(name, role, HandRaised: false);
            Context.Items["sessionId"] = sessionId;

            await Groups.AddToGroupAsync(Context.ConnectionId, Group(sessionId));
            await BroadcastRosterAsync(sessionId);
            await SendLeaderboardAsync(sessionId);
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
            await Clients.OthersInGroup(Group(sessionId)).SendAsync("Board", opJson);
        }

        // ---- chat (interactive panel) ----

        public async Task SendChat(string sessionId, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            await Clients.Group(Group(sessionId)).SendAsync("Chat", UserNameFor(sessionId), text.Trim());
        }

        // ---- quiz + leaderboard ----

        /// <summary>Teacher pushes a question to the class by index into the shared bank.</summary>
        public async Task StartQuiz(string sessionId, int questionIndex)
        {
            if (!IsTeacher)
            {
                return;
            }

            await Clients.Group(Group(sessionId)).SendAsync("QuizStarted", questionIndex);
        }

        public async Task EndQuiz(string sessionId)
        {
            if (!IsTeacher)
            {
                return;
            }

            await Clients.Group(Group(sessionId)).SendAsync("QuizEnded");
        }

        /// <summary>Student answer: correct answers score a star; the leaderboard broadcasts live.</summary>
        public async Task AnswerQuiz(string sessionId, int questionIndex, bool correct)
        {
            var name = UserNameFor(sessionId);
            if (correct)
            {
                var scores = Scores.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, int>());
                scores.AddOrUpdate(name, 1, (_, current) => current + 1);
            }

            await Clients.Group(Group(sessionId)).SendAsync("QuizAnswer", name, questionIndex, correct);
            await SendLeaderboardAsync(sessionId);
        }

        // ---- celebrations + teacher controls ----

        public async Task Celebrate(string sessionId, string? message)
        {
            if (!IsTeacher)
            {
                return;
            }

            await Clients.Group(Group(sessionId)).SendAsync("Celebrate", message);
        }

        public async Task RaiseHand(string sessionId, bool raised)
        {
            if (Rooms.TryGetValue(sessionId, out var room)
                && room.TryGetValue(Context.ConnectionId, out var state))
            {
                room[Context.ConnectionId] = state with { HandRaised = raised };
                await BroadcastRosterAsync(sessionId);
            }
        }

        /// <summary>Teacher-only board permission toggle for a participant (by connection id).</summary>
        public async Task SetBoardAccess(string sessionId, string connectionId, bool allowed)
        {
            if (!IsTeacher)
            {
                return;
            }

            await Clients.Client(connectionId).SendAsync("BoardAccess", allowed);
        }

        // ---- helpers ----

        private async Task RemoveFromSessionAsync(string sessionId)
        {
            if (Rooms.TryGetValue(sessionId, out var room))
            {
                room.TryRemove(Context.ConnectionId, out _);
                if (room.IsEmpty)
                {
                    Rooms.TryRemove(sessionId, out _);
                    Scores.TryRemove(sessionId, out _); // class over — scoreboard resets
                }
                else
                {
                    await BroadcastRosterAsync(sessionId);
                }
            }

            await Groups.RemoveFromGroupAsync(Context.ConnectionId, Group(sessionId));
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
