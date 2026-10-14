using iucs.readernest.application.Dto.Academics;

namespace iucs.readernest.application.Services
{
    public interface ILeaveCancellationReportService
    {
        /// <summary>The stored month-end snapshot if one exists, otherwise a live preview of the month so far.</summary>
        Task<LeaveCancellationReportDto> GetAsync(int year, int month, CancellationToken cancellationToken = default);

        /// <summary>Months that already have a frozen snapshot, newest first.</summary>
        Task<IReadOnlyList<LeaveCancellationReportMonthDto>> ListGeneratedAsync(CancellationToken cancellationToken = default);

        /// <summary>Computes the month and stores it, replacing any earlier snapshot of the same month.</summary>
        Task<LeaveCancellationReportDto> GenerateAsync(int year, int month, Guid? generatedBy, CancellationToken cancellationToken = default);

        /// <summary>
        /// Scheduled-job entry point: creates the snapshot for the current month once it is
        /// 23:59 IST on the last day, and for any recent earlier month still missing one (so
        /// downtime at month end can't leave a gap). Existing snapshots are never overwritten
        /// here. Returns the months generated.
        /// </summary>
        Task<IReadOnlyList<(int Year, int Month)>> EnsureSnapshotsAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
    }
}
