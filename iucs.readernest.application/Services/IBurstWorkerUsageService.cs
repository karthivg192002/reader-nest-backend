using iucs.readernest.application.Dto.Monitoring;

namespace iucs.readernest.application.Services
{
    /// <summary>
    /// Reads the Hetzner burst worker's own create/delete event log (see
    /// burst-scale-up.sh/burst-scale-down.sh) to report how often it's actually been used and
    /// an estimated cost -- there's no Hetzner billing API call involved.
    /// </summary>
    public interface IBurstWorkerUsageService
    {
        /// <summary>Null if the log couldn't be fetched (e.g. main unreachable or SSH not configured) -- callers should treat that as "unknown," not "never used."</summary>
        Task<BurstWorkerUsageDto?> GetUsageSummaryAsync(CancellationToken cancellationToken = default);
    }
}
