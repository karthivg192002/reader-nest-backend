using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Billing
{
    /// <summary>
    /// Fee-default suspension, scoped to one child when the triggering invoice is
    /// child-specific (ChildId set) -- only that child's live sessions/content/recordings
    /// are blocked, a sibling with fees current is unaffected. Null ChildId means the
    /// triggering invoice wasn't tied to a specific child (a family-level charge), so the
    /// suspension covers every child on the account instead. Access is restored
    /// automatically on payment or by admin approval.
    /// </summary>
    [Index(nameof(ParentProfileId), nameof(Status))]
    public class FeeSuspension : AuditEntity
    {
        public Guid ParentProfileId { get; set; }

        public ParentProfile ParentProfile { get; set; } = null!;

        /// <summary>Null = applies to every child on the account (the triggering invoice had no specific child).</summary>
        public Guid? ChildId { get; set; }

        public Child? Child { get; set; }

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
