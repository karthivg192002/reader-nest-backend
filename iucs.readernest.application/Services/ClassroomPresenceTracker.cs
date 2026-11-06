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

        // When each role was last connected to each session -- see IsPresentOrRecentlyLeft.
        private readonly ConcurrentDictionary<(string SessionId, string Role), DateTime> _lastLeftUtc = new();

        public void UserLeft(string sessionId, string connectionId)
        {
            if (_rooms.TryGetValue(sessionId, out var room))
            {
                if (room.TryRemove(connectionId, out var left))
                {
                    _lastLeftUtc[(sessionId, left.Role)] = DateTime.UtcNow;
                    PruneLastLeft();
                }
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
        // Jibri's "recording observer" connection (see ClassroomHub.JoinSession) is excluded
        // from every reader below -- it's a robot, not a person, and would otherwise show up as
        // a phantom "Recording" participant in the admin's live-users dashboard.
        public int TotalConnectedUsers => _rooms.Values.Sum(r => r.Values.Where(IsRealPerson).Select(v => v.UserId).Distinct().Count());

        public int ActiveClassCount => _rooms.Values.Count(room => room.Values.Any(IsRealPerson));

        public IReadOnlyList<LivePresenceEntry> GetLiveConnections() =>
            _rooms.Values.SelectMany(room => room.Values).Where(IsRealPerson).ToList();

        private static bool IsRealPerson(LivePresenceEntry entry) => entry.Role != "observer";

        // Only the last few minutes ever matter; drop entries older than a day so this in-memory map
        // can't keep growing (a few entries per class) for as long as the API runs.
        private void PruneLastLeft()
        {
            if (_lastLeftUtc.Count < 500)
            {
                return;
            }

            var cutoff = DateTime.UtcNow.AddDays(-1);
            foreach (var entry in _lastLeftUtc)
            {
                if (entry.Value < cutoff)
                {
                    _lastLeftUtc.TryRemove(entry.Key, out _);
                }
            }
        }

        public bool IsPresentOrRecentlyLeft(string sessionId, string role, TimeSpan grace)
        {
            if (_rooms.TryGetValue(sessionId, out var room) && room.Values.Any(e => e.Role == role))
            {
                return true;
            }

            return _lastLeftUtc.TryGetValue((sessionId, role), out var leftAt) && DateTime.UtcNow - leftAt <= grace;
        }
    }
}
