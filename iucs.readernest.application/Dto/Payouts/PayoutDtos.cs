using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Payouts
{
    public class PayoutRateDto
    {
        public Guid Id { get; set; }

        public Guid TeacherProfileId { get; set; }

        public string TeacherName { get; set; } = null!;

        public int DurationMinutes { get; set; }

        public decimal RatePerSession { get; set; }

        public DateOnly EffectiveFrom { get; set; }

        public bool IsActive { get; set; }
    }

    public class SavePayoutRateRequest
    {
        [Required]
        public Guid TeacherProfileId { get; set; }

        /// <summary>Allowed values: 30, 45 or 60 (validated in the service).</summary>
        [Required]
        public int DurationMinutes { get; set; }

        [Required]
        [Range(0, 9_999_999)]
        public decimal RatePerSession { get; set; }

        [Required]
        public DateOnly EffectiveFrom { get; set; }
    }

    public class PayoutItemDto
    {
        public Guid Id { get; set; }

        public Guid? ClassSessionId { get; set; }

        public PayoutItemType Type { get; set; }

        public decimal Amount { get; set; }

        public string? Note { get; set; }

        public DateTime CreatedAtUtc { get; set; }
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
