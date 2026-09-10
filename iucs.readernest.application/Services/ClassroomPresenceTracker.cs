using System.Collections.Concurrent;
using iucs.readernest.application.Common.Interfaces;

namespace iucs.readernest.application.Services
{
    /// <summary>Singleton, in-memory. See IClassroomPresenceTracker for why this exists alongside ClassroomHub's own Rooms dictionary.</summary>
    public class ClassroomPresenceTracker : IClassroomPresenceTracker
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, LivePresenceEntry>> _rooms = new();

        public void UserJoined(string sessionId, string connectionId, Guid userId, string name, string role)
        {
            var room = _rooms.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, LivePresenceEntry>());
            room[connectionId] = new LivePresenceEntry(sessionId, userId, name, role, DateTime.UtcNow);
        }

        public void UserLeft(string sessionId, string connectionId)
        {
            if (_rooms.TryGetValue(sessionId, out var room))
            {
                room.TryRemove(connectionId, out _);
                if (room.IsEmpty)
                {
                    _rooms.TryRemove(sessionId, out _);
                }
            }
        }

        // Distinct people, not raw connections -- one person open on two tabs/devices (a real,
        // observed case: a teacher with two browser tabs open to the same class) registers as
        // two connectionIds but must still count as one person, or every count downstream
        // (this KPI, GetLiveUsersAsync's per-session participant lists) overstates who's live.
        public int TotalConnectedUsers => _rooms.Values.Sum(r => r.Values.Select(v => v.UserId).Distinct().Count());

        public int ActiveClassCount => _rooms.Count;

        public IReadOnlyList<LivePresenceEntry> GetLiveConnections() =>
            _rooms.Values.SelectMany(room => room.Values).ToList();
    }
}
