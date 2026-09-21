using iucs.readernest.application.Dto.Common;
using iucs.readernest.application.Dto.Sessions;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Services
{
    /// <summary>Read side of the Class Session Logs feature — status/live/next snapshot plus
    /// the paged, filterable event trail behind the IT Admin dashboard.</summary>
    public interface IClassSessionLogService
    {
        Task<ClassSessionLogDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default);

        Task<PagedResult<ClassSessionEventLogDto>> ListEventLogsAsync(
            DateTime? fromUtc,
            DateTime? toUtc,
            Guid? sessionId,
            Guid? teacherProfileId,
            ClassSessionEventType? eventType,
            bool? expectedOnly,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ClassSessionEventLogDto>> GetSessionTimelineAsync(
            Guid sessionId, CancellationToken cancellationToken = default);
    }
}
