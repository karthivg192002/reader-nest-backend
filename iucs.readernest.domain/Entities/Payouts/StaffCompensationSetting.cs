using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Payouts
{
    /// <summary>
    /// Admin-configured compensation for one non-teacher staff member — Coordinators,
    /// Relationship Managers and Admin get a fixed monthly amount; Admission Counsellors get a
    /// percentage of their own monthly collection. Effective-dated like PayoutRate: editing the
    /// amount/percentage appends a new row rather than changing the current one in place, so a
    /// month that's never been looked at yet (and so never had its StaffPayout snapshot frozen)
    /// still computes using whatever was actually in force during that month, not whatever the
    /// setting happens to say today.
    /// </summary>
    [Index(nameof(UserId), nameof(EffectiveFrom), IsUnique = true)]
    public class StaffCompensationSetting : AuditEntity
    {
        public Guid UserId { get; set; }

        public User User { get; set; } = null!;

        public CompensationBasis Basis { get; set; }

        /// <summary>Set when Basis is FixedMonthly.</summary>
        public decimal? FixedMonthlyAmount { get; set; }

        /// <summary>Set when Basis is PercentOfMonthlyCollection — applied to the collection total after it's floored to the nearest ₹10,000.</summary>
        public decimal? CollectionPercentage { get; set; }

        public DateOnly EffectiveFrom { get; set; }
    }
}
