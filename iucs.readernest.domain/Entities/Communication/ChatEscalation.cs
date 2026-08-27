using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Communication
{
    /// <summary>
    /// A doubt the chatbot had no FAQ match for, routed to a teacher for follow-up. Any user
    /// with Communication:View (teachers, coordinators, admin) can see and resolve any open
    /// one — there is no per-teacher assignment/routing in this first pass.
    /// </summary>
    [Index(nameof(Status))]
    public class ChatEscalation : AuditEntity
    {
        public Guid UserId { get; set; }

        public User User { get; set; } = null!;

        [MaxLength(1000)]
        public string Question { get; set; } = string.Empty;

        public ChatEscalationStatus Status { get; set; } = ChatEscalationStatus.Pending;

        public string? ResolutionNote { get; set; }

        public Guid? ResolvedByUserId { get; set; }

        public User? ResolvedByUser { get; set; }

        public DateTime? ResolvedAtUtc { get; set; }
    }
}
