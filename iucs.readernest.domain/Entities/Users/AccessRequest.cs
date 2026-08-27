using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Users
{
    /// <summary>
    /// A Sub Admin's self-service request for module permissions beyond their current
    /// grant ("Request additional access" on the Relationship Manager dashboard). Purely
    /// a notify-and-track record — approving one does not itself grant anything; an Admin
    /// still makes the actual grant from Roles &amp; Permissions, same as any other
    /// permission change.
    /// </summary>
    [Index(nameof(RequestedByUserId), nameof(Status))]
    public class AccessRequest : AuditEntity
    {
        public Guid RequestedByUserId { get; set; }

        public User RequestedByUser { get; set; } = null!;

        /// <summary>Comma-separated <see cref="Enums.PermissionModule"/> names — the modules
        /// that were locked for this Sub Admin at the moment they submitted the request.</summary>
        [MaxLength(500)]
        public string RequestedModules { get; set; } = null!;

        public AccessRequestStatus Status { get; set; } = AccessRequestStatus.Pending;

        public Guid? ReviewedBy { get; set; }

        public DateTime? ReviewedAtUtc { get; set; }

        [MaxLength(500)]
        public string? ReviewNote { get; set; }
    }
}
