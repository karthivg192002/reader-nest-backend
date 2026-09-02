using iucs.readernest.domain.Entities.Settings;
using iucs.readernest.domain.Repository;

namespace iucs.readernest.application.Common
{
    /// <summary>
    /// Reads the admin's Settings → Notifications fee-collection timing ("billing.*" AppSettings).
    /// Both used to be fixed constants in BillingBackgroundService -- the payment-reminder lead
    /// time and the grace period before an overdue account gets suspended -- with no way for a
    /// centre to tune either without a code change and redeploy. A missing/invalid key falls
    /// back to the original hardcoded default, so an install where nobody has touched this
    /// setting yet behaves exactly as before it became configurable.
    /// </summary>
    public static class BillingSettings
    {
        public const string ReminderDaysBeforeDueKey = "billing.reminderDaysBeforeDue";
        public const string SuspensionGraceDaysKey = "billing.suspensionGraceDays";

        private const int DefaultReminderDaysBeforeDue = 3;
        private const int DefaultSuspensionGraceDays = 0;

        /// <summary>How many days before (or past) an invoice's due date a payment reminder goes out.</summary>
        public static async Task<int> GetReminderDaysBeforeDueAsync(
            IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
        {
            var setting = await unitOfWork.Repository<AppSetting>()
                .FirstOrDefaultAsync(s => s.Key == ReminderDaysBeforeDueKey, cancellationToken);
            return setting?.Value is { } raw && int.TryParse(raw, out var parsed) && parsed >= 0
                ? parsed
                : DefaultReminderDaysBeforeDue;
        }

        /// <summary>How many days an invoice must stay overdue before its account is auto-suspended (0 = immediately).</summary>
        public static async Task<int> GetSuspensionGraceDaysAsync(
            IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
        {
            var setting = await unitOfWork.Repository<AppSetting>()
                .FirstOrDefaultAsync(s => s.Key == SuspensionGraceDaysKey, cancellationToken);
            return setting?.Value is { } raw && int.TryParse(raw, out var parsed) && parsed >= 0
                ? parsed
                : DefaultSuspensionGraceDays;
        }
    }
}
