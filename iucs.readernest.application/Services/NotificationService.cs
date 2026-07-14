using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Dto.Communication;
using iucs.readernest.domain.Entities.Communication;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;
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

        public async Task<NotificationFeedDto> GetFeedForUserAsync(
            Guid userId,
            int take = 30,
            CancellationToken cancellationToken = default)
        {
            take = Math.Clamp(take, 1, 100);
            var repository = _unitOfWork.Repository<Notification>();

            var items = await repository.Query()
                .Where(n => n.RecipientUserId == userId)
                .OrderByDescending(n => n.CreatedAtUtc)
                .Take(take)
                .Select(n => new NotificationDto
                {
                    Id = n.Id,
                    Type = n.Type,
                    Channel = n.Channel,
                    Subject = n.Subject,
                    Body = n.Body,
                    IsRead = n.ReadAtUtc != null,
                    CreatedAtUtc = n.CreatedAtUtc,
                    ReadAtUtc = n.ReadAtUtc,
                })
                .ToListAsync(cancellationToken);

            var unreadCount = await repository.Query()
                .CountAsync(n => n.RecipientUserId == userId && n.ReadAtUtc == null, cancellationToken);

            return new NotificationFeedDto { UnreadCount = unreadCount, Items = items };
        }

        public async Task MarkReadAsync(Guid userId, Guid notificationId, CancellationToken cancellationToken = default)
        {
            var notification = await _unitOfWork.Repository<Notification>().GetByIdAsync(notificationId, cancellationToken);
            // Silently ignore unknown ids or another user's notification — a stale
            // client mustn't be able to probe or mutate other people's rows.
            if (notification is null || notification.RecipientUserId != userId || notification.ReadAtUtc != null)
            {
                return;
            }

            notification.ReadAtUtc = DateTime.UtcNow;
            _unitOfWork.Repository<Notification>().Update(notification);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task<int> MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var repository = _unitOfWork.Repository<Notification>();
            var unread = await repository.Query()
                .Where(n => n.RecipientUserId == userId && n.ReadAtUtc == null)
                .ToListAsync(cancellationToken);

            var now = DateTime.UtcNow;
            foreach (var notification in unread)
            {
                notification.ReadAtUtc = now;
                repository.Update(notification);
            }

            if (unread.Count > 0)
            {
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return unread.Count;
        }
    }
}
