using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Billing
{
    /// <summary>Recurring enrollment of a child onto a package plan; drives auto billing.</summary>
    [Index(nameof(Status))]
    public class Subscription : AuditEntity
    {
        public Guid ParentProfileId { get; set; }

        public ParentProfile ParentProfile { get; set; } = null!;

        public Guid ChildId { get; set; }

        public Child Child { get; set; } = null!;

        public Guid PackagePlanId { get; set; }

        public PackagePlan PackagePlan { get; set; } = null!;

        public SubscriptionStatus Status { get; set; } = SubscriptionStatus.Active;

        public DateOnly StartDate { get; set; }

        /// <summary>Next auto-billing run; null for non-recurring plans.</summary>
        public DateTime? NextBillingAtUtc { get; set; }

        public DateTime? CancelledAtUtc { get; set; }
    }
}
