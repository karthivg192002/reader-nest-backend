namespace iucs.readernest.application.Common.Interfaces
{
    /// <summary>
    /// Tells the recording-render bot (a separate Playwright-driven service on the Jitsi
    /// server, not part of this codebase -- see docs/JITSI_ARCHITECTURE.md's recording-render
    /// section) to start or stop capturing one room's whiteboard/quiz overlay as a video track.
    /// Best-effort by design: a class must never fail to record (or fail to join at all)
    /// because this optional enhancement's bot is slow, restarting, or unreachable.
    /// </summary>
    public interface IRecordingRenderClient
    {
        Task StartAsync(string room, CancellationToken cancellationToken = default);

        Task StopAsync(string room, CancellationToken cancellationToken = default);
    }
}
