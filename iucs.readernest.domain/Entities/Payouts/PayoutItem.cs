using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.domain.Entities.Payouts
{
    /// <summary>
    /// Line item on a monthly payout, auto-added when a class completes.
    /// Amount is signed: earnings and student no-show waiting amounts are positive,
    /// teacher no-show deductions and penalties are negative.
    /// </summary>
    public class PayoutItem : BaseEntity
    {
        public Guid PayoutId { get; set; }

        public Payout Payout { get; set; } = null!;

        public Guid? ClassSessionId { get; set; }

        public ClassSession? ClassSession { get; set; }

        public PayoutItemType Type { get; set; }

        public decimal Amount { get; set; }

        [MaxLength(500)]
        public string? Note { get; set; }

        /// <summary>
        /// Set when the teacher's captured attendance (SessionAttendance.JoinedAtUtc/LeftAtUtc)
        /// falls well short of the session's scheduled duration -- e.g. joined then left after a
        /// few minutes of a much longer class. This still accrues at the full scheduled-duration
        /// rate (no automatic proration: a dropped connection, a child needing to stop early, and
        /// a teacher genuinely cutting a class short all look identical from timestamps alone, and
        /// only a human reviewing the specific case can tell them apart) -- the flag exists so an
        /// admin sees it and can adjust the amount via AdjustItemAsync before the payout is
        /// finalized, instead of it silently paying out in full.
        /// </summary>
        public bool RequiresReview { get; set; }
    }
}
