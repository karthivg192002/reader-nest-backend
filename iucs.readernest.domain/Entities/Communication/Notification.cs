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

        public DateTime? SentAtUtc { get; set; }

        public DateTime? ReadAtUtc { get; set; }

        /// <summary>Optional structured payload (e.g. sessionId, invoiceId) as JSON.</summary>
        public string? MetadataJson { get; set; }
    }
}
