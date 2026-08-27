using iucs.readernest.domain.Entities.Settings;
using iucs.readernest.domain.Repository;

namespace iucs.readernest.application.Common
{
    /// <summary>
    /// Reads the admin's Settings → Payroll thresholds ("payroll.*" AppSettings). Both of these
    /// used to be fixed constants in code -- MinAttendancePercentForReview in PayoutService,
    /// NoShowGraceMinutes in NoShowDetectionBackgroundService -- with no way for a centre to
    /// tune either without a code change and redeploy. A missing/invalid key falls back to the
    /// original default, so a deployment where nobody has touched Settings → Payroll yet behaves
    /// exactly as before this became configurable.
    /// </summary>
    public static class PayrollSettings
    {
        public const string MinAttendancePercentForReviewKey = "payroll.minAttendancePercentForReview";
        public const string NoShowGraceMinutesKey = "payroll.noShowGraceMinutes";

        private const double DefaultMinAttendancePercentForReview = 50;
        private const double DefaultNoShowGraceMinutes = 20;

        /// <summary>Below this fraction of the scheduled duration, a SessionEarning item is flagged for review.</summary>
        public static async Task<double> GetMinAttendanceFractionForReviewAsync(
            IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
        {
            var setting = await unitOfWork.Repository<AppSetting>()
                .FirstOrDefaultAsync(s => s.Key == MinAttendancePercentForReviewKey, cancellationToken);
            var percent = setting?.Value is { } raw && double.TryParse(raw, out var parsed)
                ? parsed
                : DefaultMinAttendancePercentForReview;
            return Math.Clamp(percent, 0, 100) / 100.0;
        }

        /// <summary>How long after a session's scheduled start with nobody captured present before auto-marking a no-show.</summary>
        public static async Task<TimeSpan> GetNoShowGraceAsync(
            IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
        {
            var setting = await unitOfWork.Repository<AppSetting>()
                .FirstOrDefaultAsync(s => s.Key == NoShowGraceMinutesKey, cancellationToken);
            var minutes = setting?.Value is { } raw && double.TryParse(raw, out var parsed) && parsed > 0
                ? parsed
                : DefaultNoShowGraceMinutes;
            return TimeSpan.FromMinutes(minutes);
        }
    }
}
