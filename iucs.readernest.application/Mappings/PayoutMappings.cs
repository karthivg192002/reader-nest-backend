using iucs.readernest.application.Dto.Payouts;
using iucs.readernest.domain.Entities.Payouts;

namespace iucs.readernest.application.Mappings
{
    public static class PayoutMappings
    {
        public static PayoutRateDto ToDto(this PayoutRate rate)
        {
            return new PayoutRateDto
            {
                Id = rate.Id,
                TeacherProfileId = rate.TeacherProfileId,
                TeacherName = $"{rate.TeacherProfile.User.FirstName} {rate.TeacherProfile.User.LastName}",
                DurationMinutes = rate.DurationMinutes,
                RatePerSession = rate.RatePerSession,
                TeacherNoShowPenaltyPercent = rate.TeacherNoShowPenaltyPercent,
                EffectiveFrom = rate.EffectiveFrom,
                IsActive = rate.IsActive,
            };
        }

        public static PayoutDto ToDto(this Payout payout)
        {
            return new PayoutDto
            {
                Id = payout.Id,
                TeacherProfileId = payout.TeacherProfileId,
                TeacherName = $"{payout.TeacherProfile.User.FirstName} {payout.TeacherProfile.User.LastName}",
                PeriodYear = payout.PeriodYear,
                PeriodMonth = payout.PeriodMonth,
                Status = payout.Status,
                TotalAmount = payout.TotalAmount,
                FinalizedAtUtc = payout.FinalizedAtUtc,
                EmailSentAtUtc = payout.EmailSentAtUtc,
                Items = payout.Items
                    .OrderBy(i => i.CreatedAtUtc)
                    .Select(i => new PayoutItemDto
                    {
                        Id = i.Id,
                        ClassSessionId = i.ClassSessionId,
                        Type = i.Type,
                        Amount = i.Amount,
                        Note = i.Note,
                        CreatedAtUtc = i.CreatedAtUtc,
                    })
                    .ToList(),
            };
        }
    }
}
