using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Services
{
    public interface INotificationService
    {
        /// <summary>
        /// Records the notification and attempts immediate delivery through the
        /// configured transport; the row keeps Pending/Sent/Failed state for retries.
        /// </summary>
        Task SendEmailAsync(
            Guid recipientUserId,
            string recipientEmail,
            NotificationType type,
            string subject,
            string body,
            CancellationToken cancellationToken = default);
    }
}
