using iucs.readernest.application.Dto.Payouts;
using iucs.readernest.domain.Entities.Payouts;

namespace iucs.readernest.application.Mappings
{
    public static class StaffPayoutMappings
    {
        public static StaffCompensationSettingDto ToDto(this StaffCompensationSetting setting)
        {
            return new StaffCompensationSettingDto
            {
                UserId = setting.UserId,
                Basis = setting.Basis,
                FixedMonthlyAmount = setting.FixedMonthlyAmount,
                CollectionPercentage = setting.CollectionPercentage,
                EffectiveFrom = setting.EffectiveFrom,
                UpdatedAtUtc = setting.UpdatedAtUtc,
            };
        }

        public static StaffPayoutDto ToDto(this StaffPayout payout, string? staffName)
        {
            return new StaffPayoutDto
            {
                Id = payout.Id,
                UserId = payout.UserId,
                StaffName = staffName,
                PeriodYear = payout.PeriodYear,
                PeriodMonth = payout.PeriodMonth,
                Basis = payout.Basis,
                Amount = payout.Amount,
                CollectionAmountRaw = payout.CollectionAmountRaw,
                CollectionAmountRounded = payout.CollectionAmountRounded,
                AppliedPercentage = payout.AppliedPercentage,
                AppliedFixedAmount = payout.AppliedFixedAmount,
                IsFinalized = true,
                Status = payout.Status,
                PaidAtUtc = payout.PaidAtUtc,
            };
        }
    }
}
