using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Communication
{
    /// <summary>A parent's reply to one Bulk Email they received. One reply per recipient —
    /// the parent portal shows the reply box only until it's been used once.</summary>
    [Index(nameof(BulkEmailRecipientId), IsUnique = true)]
    public class BulkEmailReply : BaseEntity
    {
        public Guid BulkEmailRecipientId { get; set; }

        public BulkEmailRecipient BulkEmailRecipient { get; set; } = null!;

        public Guid ParentUserId { get; set; }

        public User ParentUser { get; set; } = null!;

        [MaxLength(4000)]
        public string Message { get; set; } = null!;

        public DateTime RepliedAtUtc { get; set; }
    }
}
