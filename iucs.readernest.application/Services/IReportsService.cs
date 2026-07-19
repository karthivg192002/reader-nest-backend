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

        /// <summary>Recipient count for the compose screen, resolved by the same rule the send uses.</summary>
        Task<BulkEmailResultDto> PreviewBulkEmailAsync(Guid? batchId, CancellationToken cancellationToken = default);

        /// <summary>Teacher performance view: sessions delivered, no-shows, attendance, summaries.</summary>
        Task<IReadOnlyList<TeacherPerformanceDto>> GetTeacherPerformanceAsync(CancellationToken cancellationToken = default);

        /// <summary>Student analytics with generated progress insights (attendance, quiz, engagement).</summary>
        Task<StudentAnalyticsDto> GetStudentAnalyticsAsync(Guid childId, CancellationToken cancellationToken = default);
    }
}
