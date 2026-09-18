using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.domain.Entities.Communication
{
    /// <summary>
    /// One Bulk Email "send" — the compose action an Admin fires from Bulk Email, to
    /// either every active parent or one batch's parents. Aggregate delivery counts live
    /// here; each recipient's own outcome and any reply are on <see cref="BulkEmailRecipient"/>.
    /// </summary>
    public class BulkEmailBlast : AuditEntity
    {
        public Guid SentByUserId { get; set; }

        public User SentByUser { get; set; } = null!;

        [MaxLength(200)]
        public string Subject { get; set; } = null!;

        public string Body { get; set; } = null!;

        public BulkEmailScope Scope { get; set; }

        /// <summary>Set when Scope is Batch; null for all-active-parents sends.</summary>
        public Guid? BatchId { get; set; }

        public Batch? Batch { get; set; }

        public DateTime SentAtUtc { get; set; }

        public int TotalRecipients { get; set; }

        public int SuccessCount { get; set; }

        public int FailureCount { get; set; }

        public ICollection<BulkEmailRecipient> Recipients { get; set; } = new List<BulkEmailRecipient>();
    }
}
