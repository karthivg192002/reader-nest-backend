using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Billing
{
    /// <summary>
    /// Fee-default suspension of a parent account. While Active, the parent/children
    /// cannot join live sessions or access content, and login shows the pending-fee
    /// "Pay Now" popup. Access is restored automatically on payment or by admin approval.
    /// </summary>
    [Index(nameof(ParentProfileId), nameof(Status))]
    public class FeeSuspension : AuditEntity
    {
        public Guid ParentProfileId { get; set; }

        public ParentProfile ParentProfile { get; set; } = null!;

        /// <summary>The overdue invoice that triggered the suspension.</summary>
        public Guid? InvoiceId { get; set; }

        public Invoice? Invoice { get; set; }

        [MaxLength(500)]
        public string? Reason { get; set; }

        public SuspensionStatus Status { get; set; } = SuspensionStatus.Active;

        public DateTime SuspendedAtUtc { get; set; }

        public DateTime? LiftedAtUtc { get; set; }

        /// <summary>True when payment auto-restored access, false when an admin lifted it manually.</summary>
        public bool AutoRestored { get; set; }
    }
}
