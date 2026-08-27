using System.Collections.Concurrent;
using iucs.readernest.application.Common.Interfaces;

namespace iucs.readernest.application.Services
{
    /// <summary>Singleton, in-memory. See IClassroomPresenceTracker for why this exists alongside ClassroomHub's own Rooms dictionary.</summary>
    public class ClassroomPresenceTracker : IClassroomPresenceTracker
    {
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> _rooms = new();

        public void UserJoined(string sessionId, string connectionId)
        {
            var room = _rooms.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, byte>());
            room[connectionId] = 0;
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

        public int TotalConnectedUsers => _rooms.Values.Sum(r => r.Count);

        public int ActiveClassCount => _rooms.Count;
    }
}
