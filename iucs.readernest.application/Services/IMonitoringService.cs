using iucs.readernest.application.Dto.Monitoring;

namespace iucs.readernest.application.Services
{
    public interface IMonitoringService
    {
        Task<MonitoringSummaryDto> GetSummaryAsync(CancellationToken cancellationToken = default);
    }
}
