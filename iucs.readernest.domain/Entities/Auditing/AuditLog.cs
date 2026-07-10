using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Auditing
{
    /// <summary>
    /// Append-only trail of key user actions (admin/sub-admin governance requirement).
    /// Complements the per-row CreatedBy/UpdatedBy columns on AuditEntity tables by
    /// recording the action itself, including logins, approvals, exports and payments.
    /// </summary>
    [Index(nameof(EntityName), nameof(EntityId))]
    [Index(nameof(ActorUserId))]
    public class AuditLog : BaseEntity
    {
        /// <summary>Null for unauthenticated or system actions.</summary>
        public Guid? ActorUserId { get; set; }

        public AuditAction Action { get; set; }

        [MaxLength(150)]
        public string EntityName { get; set; } = null!;

        [MaxLength(64)]
        public string? EntityId { get; set; }

        /// <summary>JSON diff or snapshot of the change.</summary>
        public string? ChangesJson { get; set; }

        [MaxLength(64)]
        public string? IpAddress { get; set; }

        [MaxLength(512)]
        public string? UserAgent { get; set; }
    }
}
