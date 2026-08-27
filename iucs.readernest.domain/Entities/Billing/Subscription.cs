using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Billing
{
    /// <summary>Recurring enrollment of a child onto a package plan; drives auto billing.</summary>
    // Composite rather than Status alone, mirroring Invoice's (Status, DueDate): the hourly
    // billing sweep asks for "Active AND NextBillingAtUtc <= now", and a Status-only index
    // matches every active subscription — i.e. almost the whole table once the product has
    // traction — leaving the date to be checked row by row. Leading on Status keeps every
    // status-only query (subscription list filters) covered by the same index.
    [Index(nameof(Status), nameof(NextBillingAtUtc))]
    // Same reasoning as the index above, for the expiry sweep's own "Active AND EndDate <= today" query.
    [Index(nameof(Status), nameof(EndDate))]
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

        /// <summary>StartDate + PackagePlan.ValidityDays at creation time; null when the plan has no set validity window.</summary>
        public DateOnly? EndDate { get; set; }

        /// <summary>Next auto-billing run; null for non-recurring plans.</summary>
        public DateTime? NextBillingAtUtc { get; set; }

        public DateTime? CancelledAtUtc { get; set; }
    }
}
