using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Communication
{
    /// <summary>One message in a <see cref="SupportTicket"/> thread, from the parent or a staff member.</summary>
    [Index(nameof(SupportTicketId), nameof(CreatedAtUtc))]
    public class SupportTicketMessage : AuditEntity
    {
        public Guid SupportTicketId { get; set; }

        public SupportTicket SupportTicket { get; set; } = null!;

        public Guid AuthorUserId { get; set; }

        public User AuthorUser { get; set; } = null!;

        /// <summary>Written by the team (RM/Admin) rather than the parent.</summary>
        public bool IsStaff { get; set; }

        [MaxLength(4000)]
        public string Body { get; set; } = string.Empty;
    }
}
