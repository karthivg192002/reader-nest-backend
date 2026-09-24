using iucs.readernest.domain.Entities.Settings;
using iucs.readernest.domain.Repository;

namespace iucs.readernest.application.Common
{
    /// <summary>
    /// Reads the admin's Settings → Payroll thresholds ("payroll.*" AppSettings). NoShowGraceMinutes
    /// used to be a fixed constant in NoShowDetectionBackgroundService, with no way for a centre to
    /// tune it without a code change and redeploy. (The old "minimum attendance %" review threshold
    /// is gone: any class that runs shorter than scheduled now goes to Payout Approvals.) A
    /// missing/invalid key falls back to the
    /// original default, so a deployment where nobody has touched Settings → Payroll yet behaves
    /// exactly as before this became configurable.
    /// </summary>
    public static class PayrollSettings
    {
        public const string NoShowGraceMinutesKey = "payroll.noShowGraceMinutes";

        private const double DefaultNoShowGraceMinutes = 20;

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
