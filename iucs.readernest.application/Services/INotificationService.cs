using iucs.readernest.application.Dto.Communication;
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

        /// <summary>The signed-in user's most recent notifications and unread count, for the notification bell.</summary>
        Task<NotificationFeedDto> GetFeedForUserAsync(Guid userId, int take = 30, CancellationToken cancellationToken = default);

        /// <summary>Marks a single notification read; no-ops unless it belongs to the given user.</summary>
        Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default);

        /// <summary>Marks every unread notification for the user read; returns how many changed.</summary>
        Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default);
    }
}
