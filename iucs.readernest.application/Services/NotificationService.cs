using iucs.readernest.application.Common;
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
        private readonly IEmailTemplateService _emailTemplateService;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(
            IUnitOfWork unitOfWork,
            IEmailSender emailSender,
            IEmailTemplateService emailTemplateService,
            ILogger<NotificationService> logger)
        {
            _unitOfWork = unitOfWork;
            _emailSender = emailSender;
            _emailTemplateService = emailTemplateService;
            _logger = logger;
        }

        public async Task<NotificationStatus> SendEmailAsync(
            Guid recipientUserId,
            string recipientEmail,
            NotificationType type,
            string subject,
            string body,
            Guid? bulkEmailRecipientId = null,
            CancellationToken cancellationToken = default)
        {
            return await SendRenderedEmailAsync(
                recipientUserId, recipientEmail, type, subject, body, null, bulkEmailRecipientId, cancellationToken);
        }

        public async Task SendTemplatedEmailAsync(
            Guid recipientUserId,
            string recipientEmail,
            NotificationType type,
            string templateKey,
            IReadOnlyDictionary<string, string> tokens,
            CancellationToken cancellationToken = default)
        {
            var (subject, body) = await _emailTemplateService.RenderAsync(templateKey, tokens, cancellationToken);
            await SendRenderedEmailAsync(recipientUserId, recipientEmail, type, subject, body, templateKey, null, cancellationToken);
        }

        private async Task<NotificationStatus> SendRenderedEmailAsync(
            Guid recipientUserId,
            string recipientEmail,
            NotificationType type,
            string subject,
            string body,
            string? templateKey,
            Guid? bulkEmailRecipientId,
            CancellationToken cancellationToken)
        {
            var notification = new Notification
            {
                RecipientUserId = recipientUserId,
                Type = type,
                TemplateKey = templateKey,
                Channel = NotificationChannel.Email,
                Subject = subject,
                // The in-app bell feed shows this Body directly as text, but every templated
                // email is rendered from EmailTemplateSeedData.Wrap()'s full branded HTML shell —
                // callers here only ever pass that (or the equivalent hand-built HTML from
                // SendEmailAsync), never plain text. Store a stripped-down plain-text copy so
                // the feed reads as a message instead of raw markup; the real HTML still goes
                // out over email via _emailSender.SendAsync(body) below, unchanged.
                Body = HtmlText.PlainTextFromHtml(body),
                BulkEmailRecipientId = bulkEmailRecipientId,
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

            // Callers routinely send a notification after their own SaveChangesAsync has
            // already run (e.g. as a side effect once the business entity is committed), so
            // this row must persist itself rather than rely on a save that may never come.
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return notification.Status;
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
                    TemplateKey = n.TemplateKey,
                    Channel = n.Channel,
                    Subject = n.Subject,
                    Body = n.Body,
                    IsRead = n.ReadAtUtc != null,
                    CreatedAtUtc = n.CreatedAtUtc,
                    ReadAtUtc = n.ReadAtUtc,
                    BulkEmailRecipientId = n.BulkEmailRecipientId,
                    HasReplied = n.BulkEmailRecipientId != null && n.BulkEmailRecipient!.Reply != null,
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

            // A single conditional UPDATE, not "SELECT every unread row, then one UPDATE
            // statement each". A long-lived account's unread backlog is unbounded, and the
            // old shape materialised all of it just to stamp one column. ExecuteUpdateAsync
            // bypasses the change tracker, so the audit interceptor never sees these writes —
            // stamp UpdatedAtUtc by hand the way it would have (Notification is a plain
            // BaseEntity, so there is no UpdatedBy to set).
            var now = DateTime.UtcNow;
            return await repository.ExecuteUpdateAsync(
                n => n.RecipientUserId == userId && n.ReadAtUtc == null,
                setters => setters
                    .SetProperty(n => n.ReadAtUtc, now)
                    .SetProperty(n => n.UpdatedAtUtc, now),
                cancellationToken);
        }
    }
}
