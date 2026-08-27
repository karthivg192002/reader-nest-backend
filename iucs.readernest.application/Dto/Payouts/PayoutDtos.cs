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

        /// <summary>Teacher's captured attendance fell well short of the scheduled duration — needs a human look before this payout is finalized.</summary>
        public bool RequiresReview { get; set; }
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
