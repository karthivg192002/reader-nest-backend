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
        void UserJoined(string sessionId, string connectionId);
        void UserLeft(string sessionId, string connectionId);

        /// <summary>Total connections currently joined to any live class, platform-wide.</summary>
        int TotalConnectedUsers { get; }

        /// <summary>Number of distinct sessions with at least one connected participant.</summary>
        int ActiveClassCount { get; }
    }
}
