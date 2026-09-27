using iucs.readernest.application.Dto.Payouts;
using iucs.readernest.domain.Entities.Payouts;
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
        Task<PayoutItem> AccrueForSessionAsync(ClassSession session, PayoutItemType type, string? note, CancellationToken cancellationToken = default);

        /// <summary>
        /// Emails Admin and every payout approver (Management / Founder) that a class ran short
        /// and is waiting in Payout Approvals — scheduled time, scheduled vs actual duration,
        /// shortfall, teacher and batch/students. Call after the item has been saved.
        /// </summary>
        Task NotifyPayoutReviewAsync(Guid payoutItemId, CancellationToken cancellationToken = default);

        /// <summary>Payout Approvals: classes awaiting a decision (pending) or already decided, newest first.</summary>
        Task<IReadOnlyList<PayoutApprovalDto>> ListApprovalsAsync(bool pending, CancellationToken cancellationToken = default);

        /// <summary>
        /// Approve a flagged class for full payout, for a partial payout (a given amount, or
        /// pro-rated to the delivered minutes), or reject it (nothing paid). Only while its
        /// payout is still Pending; the payout can't be finalized until every flag is decided.
        /// </summary>
        Task<PayoutApprovalDto> DecideApprovalAsync(Guid payoutItemId, Guid reviewerUserId, DecidePayoutApprovalRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Admin correction to one accrued line item -- the only way to act on a
        /// RequiresReview flag (e.g. a teacher's attendance fell well short of the scheduled
        /// class). Only while the item's payout is still Pending; recalculates the payout's
        /// TotalAmount from the corrected item and clears the review flag.
        /// </summary>
        Task<PayoutDto> AdjustItemAsync(Guid payoutId, Guid itemId, AdjustPayoutItemRequest request, CancellationToken cancellationToken = default);

        /// <summary>Locks the month's total and emails the statement to the teacher.</summary>
        Task<PayoutDto> FinalizeAsync(Guid payoutId, CancellationToken cancellationToken = default);

        Task<PayoutDto> MarkPaidAsync(Guid payoutId, CancellationToken cancellationToken = default);
    }
}
