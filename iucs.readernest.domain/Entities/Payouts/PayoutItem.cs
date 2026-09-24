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
        /// Set when the class ran for less than its scheduled duration (the teacher's captured
        /// attendance inside the scheduled window — e.g. a 3:00–3:30 class the teacher left at
        /// 3:28 delivered 28 of 30 minutes), or when no teacher attendance was recorded at all.
        /// Any shortfall counts: such a class is never treated as a normal completed class. The
        /// item still accrues at the full scheduled-duration rate (a dropped connection, a child
        /// needing to stop early and a teacher cutting a class short look identical from
        /// timestamps alone), but it goes to Payout Approvals and blocks finalizing until Admin /
        /// Management approve it in full, approve it partially, or reject it.
        /// </summary>
        public bool RequiresReview { get; set; }

        /// <summary>The class's scheduled length, captured when a SessionEarning accrues.</summary>
        public int? ScheduledMinutes { get; set; }

        /// <summary>Minutes the teacher was actually in class inside the scheduled window; null when no attendance was recorded.</summary>
        public int? DeliveredMinutes { get; set; }

        /// <summary>Amount as accrued, before an approval decision changed it.</summary>
        public decimal? AmountBeforeReview { get; set; }

        public PayoutReviewDecision? ReviewDecision { get; set; }

        public Guid? ReviewedByUserId { get; set; }

        public DateTime? ReviewedAtUtc { get; set; }
    }
}
