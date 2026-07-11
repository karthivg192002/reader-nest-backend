using iucs.readernest.application.Dto.Reports;

namespace iucs.readernest.application.Services
{
    public interface IReportsService
    {
        /// <summary>Admin BI dashboard aggregates: students, revenue, conversion, occupancy, utilization.</summary>
        Task<DashboardSummaryDto> GetDashboardSummaryAsync(CancellationToken cancellationToken = default);

        /// <summary>CSV exports for the centralized reports (attendance | revenue | payouts | conversion).</summary>
        Task<string> ExportCsvAsync(string report, CancellationToken cancellationToken = default);

        /// <summary>Bulk email to all active parents, or to the parents of one batch.</summary>
        Task<BulkEmailResultDto> SendBulkEmailAsync(BulkEmailRequest request, CancellationToken cancellationToken = default);
    }
}
