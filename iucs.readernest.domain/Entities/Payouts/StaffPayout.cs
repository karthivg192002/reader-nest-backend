using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Payouts
{
    /// <summary>
    /// One non-teacher staff member's computed pay for one calendar month — the historical
    /// record that must stay put even if their compensation setting is edited later. The current
    /// month is always computed live (never a row here) so it reflects today's setting and
    /// today's collections; the first time a PAST month is looked at (by the staff member or an
    /// admin) it's computed once from that month's actual collections/setting-at-the-time and
    /// saved here, after which it never changes again — same "close the books" idea as the
    /// teacher Payout's Pending→Finalized step, just without an accrual phase since there's
    /// nothing to accrue line-by-line.
    /// </summary>
    [Index(nameof(UserId), nameof(PeriodYear), nameof(PeriodMonth), IsUnique = true)]
    public class StaffPayout : AuditEntity
    {
        public Guid UserId { get; set; }

        public User User { get; set; } = null!;

        public int PeriodYear { get; set; }

        public int PeriodMonth { get; set; }

        public CompensationBasis Basis { get; set; }

        /// <summary>Final payable amount for the month.</summary>
        public decimal Amount { get; set; }

        /// <summary>PercentOfMonthlyCollection only — the raw collected total before flooring.</summary>
        public decimal? CollectionAmountRaw { get; set; }

        /// <summary>PercentOfMonthlyCollection only — the collected total floored to the nearest ₹10,000, i.e. what the percentage was actually applied to.</summary>
        public decimal? CollectionAmountRounded { get; set; }

        /// <summary>The percentage actually applied, captured so a later edit to the setting can't retroactively change this month's story.</summary>
        public decimal? AppliedPercentage { get; set; }

        /// <summary>FixedMonthly only — the flat amount actually applied.</summary>
        public decimal? AppliedFixedAmount { get; set; }

        public StaffPayoutStatus Status { get; set; } = StaffPayoutStatus.Finalized;

        public DateTime? PaidAtUtc { get; set; }
    }
}
