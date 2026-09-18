using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Communication
{
    /// <summary>One recipient of one <see cref="BulkEmailBlast"/> and their delivery outcome.
    /// Status reuses <see cref="NotificationStatus"/>'s Sent/Failed so it matches the same
    /// vocabulary the underlying <see cref="Notification"/> row was written with.</summary>
    [Index(nameof(BulkEmailBlastId))]
    public class BulkEmailRecipient : BaseEntity
    {
        public Guid BulkEmailBlastId { get; set; }

        public BulkEmailBlast BulkEmailBlast { get; set; } = null!;

        public Guid RecipientUserId { get; set; }

        public User RecipientUser { get; set; } = null!;

        [MaxLength(320)]
        public string Email { get; set; } = null!;

        public NotificationStatus Status { get; set; }

        [MaxLength(500)]
        public string? ErrorMessage { get; set; }

        public DateTime? SentAtUtc { get; set; }

        public BulkEmailReply? Reply { get; set; }
    }
}
