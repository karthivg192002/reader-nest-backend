namespace iucs.readernest.application.Common.Interfaces
{
    /// <summary>
    /// Platform-wide "who's actually in a live class right now" count, fed by ClassroomHub's
    /// own join/leave lifecycle. Deliberately separate from the Hub's own per-room Rooms
    /// dictionary (which drives roster/leaderboard UI) rather than reading it directly —
    /// the application layer can't reference the API layer's Hub, so this is the seam.
    /// In-memory only, same as the Hub's own state: nothing here needs to survive a restart.
    /// </summary>
    public interface IClassroomPresenceTracker
    {
        void UserJoined(string sessionId, string connectionId, Guid userId, string name, string role);
        void UserLeft(string sessionId, string connectionId);

        /// <summary>Total connections currently joined to any live class, platform-wide.</summary>
        int TotalConnectedUsers { get; }

        /// <summary>Number of distinct sessions with at least one connected participant.</summary>
        int ActiveClassCount { get; }

        /// <summary>Every currently-connected classroom participant, for an admin "who's live right now" view.</summary>
        IReadOnlyList<LivePresenceEntry> GetLiveConnections();
    }

    /// <summary>One connected participant. SessionId is the raw string ClassroomHub joins with (a ClassSession Guid as text).</summary>
    public record LivePresenceEntry(string SessionId, Guid UserId, string Name, string Role, DateTime JoinedAtUtc);
}
