using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Communication
{
    /// <summary>
    /// A parent's concern raised through the portal ("Help &amp; Support"), handled by the
    /// Relationship Managers and Admin as a message thread. Replaces WhatsApp/phone contact
    /// between families and the team, so every conversation stays on record. Any staff user
    /// with SupportTickets:View sees every ticket — there is no per-RM assignment yet.
    /// </summary>
    [Index(nameof(ParentUserId), nameof(LastActivityAtUtc))]
    [Index(nameof(Status), nameof(LastActivityAtUtc))]
    public class SupportTicket : AuditEntity
    {
        public Guid ParentUserId { get; set; }

        public User ParentUser { get; set; } = null!;

        /// <summary>The child the concern is about, when the parent picked one.</summary>
        public Guid? ChildId { get; set; }

        public Child? Child { get; set; }

        public SupportTicketCategory Category { get; set; }

        [MaxLength(200)]
        public string Subject { get; set; } = string.Empty;

        public SupportTicketStatus Status { get; set; } = SupportTicketStatus.Open;

        /// <summary>Last message or status change — orders both the parent's list and the RM queue.</summary>
        public DateTime LastActivityAtUtc { get; set; }

        /// <summary>True when the parent wrote last, i.e. the ticket is waiting on the team.</summary>
        public bool AwaitingStaffReply { get; set; } = true;

        public DateTime? ResolvedAtUtc { get; set; }

        public ICollection<SupportTicketMessage> Messages { get; set; } = new List<SupportTicketMessage>();
    }
}
