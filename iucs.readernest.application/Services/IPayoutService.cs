using iucs.readernest.application.Dto.Payouts;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Services
{
    public interface IPayoutService
    {
        Task<IReadOnlyList<PayoutRateDto>> ListRatesAsync(Guid? teacherProfileId, CancellationToken cancellationToken = default);

        /// <summary>Rate changes append a new effective-dated row; history stays reproducible.</summary>
        Task<PayoutRateDto> SetRateAsync(SavePayoutRateRequest request, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<PayoutDto>> ListAsync(
            int? year,
            int? month,
            Guid? teacherProfileId,
            CancellationToken cancellationToken = default);

        /// <summary>Visibility rule: a teacher sees only their own payouts.</summary>
        Task<IReadOnlyList<PayoutDto>> ListForTeacherUserAsync(Guid userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds a line item to the teacher's current-month payout for a session event.
        /// Amount derives from the teacher's effective per-duration rate; deductions are negative.
        /// Does not save — participates in the caller's unit of work.
        /// </summary>
        Task AccrueForSessionAsync(ClassSession session, PayoutItemType type, string? note, CancellationToken cancellationToken = default);

        /// <summary>Locks the month's total and emails the statement to the teacher.</summary>
        Task<PayoutDto> FinalizeAsync(Guid payoutId, CancellationToken cancellationToken = default);

        Task<PayoutDto> MarkPaidAsync(Guid payoutId, CancellationToken cancellationToken = default);
    }
}
