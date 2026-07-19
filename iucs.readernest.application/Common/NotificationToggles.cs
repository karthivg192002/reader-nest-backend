using iucs.readernest.domain.Entities.Settings;
using iucs.readernest.domain.Repository;

namespace iucs.readernest.application.Common
{
    /// <summary>
    /// Reads the admin's Settings → Notifications toggles ("notify.*" AppSettings) for the
    /// senders they govern. A missing key counts as enabled, matching the seeded defaults —
    /// only an explicit "false" (the admin turned it off) silences a sender.
    /// </summary>
    public static class NotificationToggles
    {
        public const string FeeReminders = "notify.feeReminders";
        public const string LeaveRequests = "notify.leaveRequests";
        public const string WeeklyDigest = "notify.weeklyDigest";

        public static async Task<bool> IsEnabledAsync(
            IUnitOfWork unitOfWork,
            string key,
            CancellationToken cancellationToken = default)
        {
            var setting = await unitOfWork.Repository<AppSetting>()
                .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
            return !string.Equals(setting?.Value?.Trim(), "false", StringComparison.OrdinalIgnoreCase);
        }
    }
}
