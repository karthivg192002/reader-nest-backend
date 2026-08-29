using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Communication
{
    /// <summary>
    /// Outbound message (email/SMS/in-app): reminders, alerts, payment notices,
    /// payout statements and bulk mail. High-volume and system-generated, so BaseEntity.
    /// </summary>
    [Index(nameof(RecipientUserId), nameof(Status))]
    public class Notification : BaseEntity
    {
        public Guid RecipientUserId { get; set; }

        public User RecipientUser { get; set; } = null!;

        public NotificationType Type { get; set; }

        public NotificationChannel Channel { get; set; }

        [MaxLength(200)]
        public string? Subject { get; set; }

        public string Body { get; set; } = null!;

        public NotificationStatus Status { get; set; } = NotificationStatus.Pending;

        /// <summary>
        /// The EmailTemplateSeedData key this was rendered from (e.g. "batch-assignment",
        /// "leave-submitted-admin-alert"), null for hand-built emails (SendEmailAsync/bulk
        /// mail). Type alone is too coarse to route a click to the right page — a third of
        /// templates share NotificationType.General but are about very different things
        /// (batch assignment, leave submitted, KPI digest, access requests...); this is the
        /// stable, non-interpolated signal the frontend bell/notifications list keys its
        /// per-notification navigation off instead of parsing the templated Subject text.
        /// </summary>
        [MaxLength(100)]
        public string? TemplateKey { get; set; }

        public DateTime? SentAtUtc { get; set; }

        public DateTime? ReadAtUtc { get; set; }

        /// <summary>Optional structured payload (e.g. sessionId, invoiceId) as JSON.</summary>
        public string? MetadataJson { get; set; }
    }
}
