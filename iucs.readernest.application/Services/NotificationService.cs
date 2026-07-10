using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.domain.Entities.Communication;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.Extensions.Logging;

namespace iucs.readernest.application.Services
{
    public class NotificationService : INotificationService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(
            IUnitOfWork unitOfWork,
            IEmailSender emailSender,
            ILogger<NotificationService> logger)
        {
            _unitOfWork = unitOfWork;
            _emailSender = emailSender;
            _logger = logger;
        }

        public async Task SendEmailAsync(
            Guid recipientUserId,
            string recipientEmail,
            NotificationType type,
            string subject,
            string body,
            CancellationToken cancellationToken = default)
        {
            var notification = new Notification
            {
                RecipientUserId = recipientUserId,
                Type = type,
                Channel = NotificationChannel.Email,
                Subject = subject,
                Body = body,
            };

            try
            {
                await _emailSender.SendAsync(recipientEmail, subject, body, cancellationToken);
                notification.Status = NotificationStatus.Sent;
                notification.SentAtUtc = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                // Delivery failure must not fail the business operation; the row
                // stays Failed for a retry job (Sprint 2 hardening).
                _logger.LogError(ex, "Email delivery failed for user {UserId} ({Type})", recipientUserId, type);
                notification.Status = NotificationStatus.Failed;
            }

            await _unitOfWork.Repository<Notification>().AddAsync(notification, cancellationToken);
        }
    }
}
