using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Payouts
{
    /// <summary>A staff member Admin/Management can set compensation for, with whatever's currently configured (if any).</summary>
    public class EligibleStaffDto
    {
        public Guid UserId { get; set; }

        public string Name { get; set; } = null!;

        public string Email { get; set; } = null!;

        public UserRole Role { get; set; }

        /// <summary>The Sub Admin's assigned preset (e.g. "Coordinator", "Parent Relationship Manager") — null for Admin/Admission accounts, which have no preset.</summary>
        public string? RoleName { get; set; }

        public StaffCompensationSettingDto? Compensation { get; set; }
    }

    public class StaffCompensationSettingDto
    {
        public Guid UserId { get; set; }

        public CompensationBasis Basis { get; set; }

        public decimal? FixedMonthlyAmount { get; set; }

        public decimal? CollectionPercentage { get; set; }

        public DateOnly EffectiveFrom { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }
    }

    /// <summary>Admin/Management sets or edits one staff member's compensation — takes effect from EffectiveFrom (defaults to today) onward; months before that keep whatever was in force at the time.</summary>
    public class SaveStaffCompensationRequest : IValidatableObject
    {
        [Required]
        public CompensationBasis Basis { get; set; }

        [Range(0, 99_999_999)]
        public decimal? FixedMonthlyAmount { get; set; }

        [Range(0, 100)]
        public decimal? CollectionPercentage { get; set; }

        public DateOnly? EffectiveFrom { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (Basis == CompensationBasis.FixedMonthly && FixedMonthlyAmount is null)
            {
                yield return new ValidationResult(
                    "FixedMonthlyAmount is required when Basis is FixedMonthly.", [nameof(FixedMonthlyAmount)]);
            }

            if (Basis == CompensationBasis.PercentOfMonthlyCollection && CollectionPercentage is null)
            {
                yield return new ValidationResult(
                    "CollectionPercentage is required when Basis is PercentOfMonthlyCollection.", [nameof(CollectionPercentage)]);
            }
        }
    }

    /// <summary>One month's pay for a non-teacher staff member — live (current month, not yet saved) or a frozen past-month snapshot.</summary>
    public class StaffPayoutDto
    {
        /// <summary>Null for the live current month, which has no row yet.</summary>
        public Guid? Id { get; set; }

        public Guid UserId { get; set; }

        public string? StaffName { get; set; }

        public int PeriodYear { get; set; }

        public int PeriodMonth { get; set; }

        public CompensationBasis Basis { get; set; }

        public decimal Amount { get; set; }

        public decimal? CollectionAmountRaw { get; set; }

        public decimal? CollectionAmountRounded { get; set; }

        public decimal? AppliedPercentage { get; set; }

        public decimal? AppliedFixedAmount { get; set; }

        /// <summary>False for the live current month (nothing saved yet, so nothing to mark paid).</summary>
        public bool IsFinalized { get; set; }

        public StaffPayoutStatus? Status { get; set; }

        public DateTime? PaidAtUtc { get; set; }
    }

    /// <summary>The staff member's own earnings view: the live current month plus every past month on record.</summary>
    public class StaffEarningsDto
    {
        public StaffPayoutDto CurrentMonth { get; set; } = null!;

        public IReadOnlyList<StaffPayoutDto> History { get; set; } = [];
    }
}
