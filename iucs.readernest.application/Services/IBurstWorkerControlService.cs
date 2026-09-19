using iucs.readernest.application.Dto.Monitoring;

namespace iucs.readernest.application.Services
{
    /// <summary>
    /// The dashboard's manual override for the on-demand Hetzner burst worker -- for an emergency
    /// (an incident, a known heavy class, the automatic capacity check misbehaving) where an admin
    /// wants extra recording capacity NOW instead of waiting for the automatic trigger.
    /// </summary>
    public interface IBurstWorkerControlService
    {
        /// <summary>
        /// Creates the burst worker immediately (a no-op if one already exists) and holds it up for
        /// <paramref name="holdMinutes"/> (clamped 15-480) so the automatic teardown doesn't delete it
        /// while it's still idle. Pressing it again while running extends the hold.
        /// </summary>
        Task<BurstWorkerControlResultDto> StartAsync(int holdMinutes, string requestedBy, CancellationToken cancellationToken = default);

        /// <summary>
        /// Clears any manual hold and runs the normal teardown: the server is deleted only if every
        /// recorder is idle AND every recording on it is fully on main. Otherwise it reports why not
        /// and the automatic cycle deletes it as soon as that becomes true. There is deliberately no
        /// "force delete" -- that could destroy a recording in progress.
        /// </summary>
        Task<BurstWorkerControlResultDto> StopAsync(CancellationToken cancellationToken = default);
    }
}
