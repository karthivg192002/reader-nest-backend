using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;

namespace iucs.readernest.domain.Entities.Payouts
{
    /// <summary>
    /// One manual correction to a teacher <see cref="Payout"/> (a line item's amount changed,
    /// or an addition/deduction line added) or to a <see cref="StaffPayout"/>'s amount. Kept
    /// as its own history so a payout stays correctable after it was finalized or paid while
    /// the original amount, the revised amount, who changed it and why all stay on record.
    /// CreatedBy/CreatedAtUtc are who and when.
    /// </summary>
    public class PayoutChange : AuditEntity
    {
        public Guid? PayoutId { get; set; }

        public Payout? Payout { get; set; }

        /// <summary>The teacher payout line this change was made to; null for a staff payout.</summary>
        public Guid? PayoutItemId { get; set; }

        public Guid? StaffPayoutId { get; set; }

        public StaffPayout? StaffPayout { get; set; }

        /// <summary>What was changed, in words — e.g. "Session earning, 12 Oct (Phonics A)".</summary>
        [MaxLength(300)]
        public string Description { get; set; } = null!;

        [MaxLength(500)]
        public string Reason { get; set; } = null!;

        /// <summary>The line's (or staff payout's) amount before and after this change.</summary>
        public decimal AmountBefore { get; set; }

        public decimal AmountAfter { get; set; }

        /// <summary>The whole payout's payable total before and after this change.</summary>
        public decimal TotalBefore { get; set; }

        public decimal TotalAfter { get; set; }
    }
}
