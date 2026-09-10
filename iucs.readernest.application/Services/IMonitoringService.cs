using iucs.readernest.application.Dto.Monitoring;

namespace iucs.readernest.application.Services
{
    public interface IMonitoringService
    {
        Task<MonitoringSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// CPU/memory history for one server over an admin-selected window. "1h" (~2-minute
        /// steps, the same data GetSummaryAsync's CpuHistory/MemoryHistory already carry),
        /// "24h" (~15-minute steps), or "7d" (~2-hour steps) -- step size is scaled to the
        /// window so the point count stays chart-sized instead of growing unbounded. Throws
        /// <see cref="ArgumentException"/> for an unknown server name or range.
        /// </summary>
        Task<HistoryRangeDto> GetHistoryAsync(string serverName, string range, CancellationToken cancellationToken = default);

        /// <summary>Every live class session with a connected participant right now, and who's in it -- an admin-only "who's live" view.</summary>
        Task<List<LiveClassSessionDto>> GetLiveUsersAsync(CancellationToken cancellationToken = default);
    }
}
