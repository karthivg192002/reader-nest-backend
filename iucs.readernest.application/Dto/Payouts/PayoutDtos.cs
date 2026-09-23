using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Payouts
{
    public class PayoutRateDto
    {
        public Guid Id { get; set; }

        /// <summary>Null identifies the centre-wide default rate card.</summary>
        public Guid? TeacherProfileId { get; set; }

        public string TeacherName { get; set; } = null!;

        public decimal RatePerMinute { get; set; }

        public decimal TeacherNoShowPenaltyPercent { get; set; }

        public DateOnly EffectiveFrom { get; set; }

        public bool IsActive { get; set; }
    }

    public class SavePayoutRateRequest
    {
        /// <summary>Omit (null) to save the centre-wide default rate card that pays teachers without their own rates.</summary>
        public Guid? TeacherProfileId { get; set; }

        [Required]
        [Range(0, 9_999_999)]
        public decimal RatePerMinute { get; set; }

        /// <summary>Teacher no-show deduction as % of the session rate (100 = full rate; 0 disables the deduction).</summary>
        [Range(0, 300)]
        public decimal TeacherNoShowPenaltyPercent { get; set; } = 100m;

        [Required]
        public DateOnly EffectiveFrom { get; set; }
    }

    public class PayoutItemDto
    {
        public Guid Id { get; set; }

        public Guid? ClassSessionId { get; set; }

        /// <summary>The batch this item's class belongs to — null for items with no ClassSessionId (a bonus/adjustment with no single class behind it).</summary>
        public string? ClassName { get; set; }

        /// <summary>The class's own scheduled start, not when this payout item was created — lets a teacher see earnings "class wise" by actual session date.</summary>
        public DateTime? SessionDate { get; set; }

        public PayoutItemType Type { get; set; }

        public decimal Amount { get; set; }

        public string? Note { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        /// <summary>The class ran shorter than scheduled (or no attendance was recorded) — awaiting an Admin / Management decision in Payout Approvals before this payout can be finalized.</summary>
        public bool RequiresReview { get; set; }

        public int? ScheduledMinutes { get; set; }

        public int? DeliveredMinutes { get; set; }

        public PayoutReviewDecision? ReviewDecision { get; set; }
    }

    /// <summary>One short / unattended class awaiting (or given) a payout decision — a row on the Payout Approvals screen.</summary>
    public class PayoutApprovalDto
    {
        public Guid ItemId { get; set; }

        public Guid PayoutId { get; set; }

        public PayoutStatus PayoutStatus { get; set; }

        public Guid? ClassSessionId { get; set; }

        public string TeacherName { get; set; } = null!;

        /// <summary>Batch name, "Demo class", or "Class session".</summary>
        public string ClassName { get; set; } = null!;

        /// <summary>Enrolled students (batch) or the demo child's name.</summary>
        public string? StudentNames { get; set; }

        public DateTime? ScheduledStartAtUtc { get; set; }

        public DateTime? ScheduledEndAtUtc { get; set; }

        public DateTime? ActualStartAtUtc { get; set; }

        public DateTime? ActualEndAtUtc { get; set; }

        public int? ScheduledMinutes { get; set; }

        /// <summary>Null when no teacher attendance was recorded at all.</summary>
        public int? DeliveredMinutes { get; set; }

        public int? ShortfallMinutes { get; set; }

        /// <summary>Full scheduled-duration amount as accrued.</summary>
        public decimal FullAmount { get; set; }

        /// <summary>Full amount pro-rated to the delivered minutes — the suggested partial payout.</summary>
        public decimal ProRatedAmount { get; set; }

        /// <summary>Current amount on the payout (after any decision).</summary>
        public decimal Amount { get; set; }

        public bool Pending { get; set; }

        public PayoutReviewDecision? Decision { get; set; }

        public string? ReviewedByName { get; set; }

        public DateTime? ReviewedAtUtc { get; set; }

        public string? Note { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }

    public class DecidePayoutApprovalRequest
    {
        [Required]
        public PayoutReviewDecision Decision { get; set; }

        /// <summary>Partial approvals only: the amount to pay. Omitted = pro-rated to the delivered minutes.</summary>
        [Range(0, 99_999_999)]
        public decimal? Amount { get; set; }

        [MaxLength(300)]
        public string? Note { get; set; }
    }

    /// <summary>Admin correction to one line item — only while its payout is still Pending.</summary>
    public class AdjustPayoutItemRequest
    {
        [Required]
        public decimal NewAmount { get; set; }

        [Required]
        [MaxLength(500)]
        public string Reason { get; set; } = null!;
    }

    public class PayoutDto
    {
        public Guid Id { get; set; }

        public Guid TeacherProfileId { get; set; }

        public string TeacherName { get; set; } = null!;

        public int PeriodYear { get; set; }

        public int PeriodMonth { get; set; }

        public PayoutStatus Status { get; set; }

        public decimal TotalAmount { get; set; }

        public DateTime? FinalizedAtUtc { get; set; }

        public DateTime? EmailSentAtUtc { get; set; }

        public IReadOnlyList<PayoutItemDto> Items { get; set; } = [];
    }
}
