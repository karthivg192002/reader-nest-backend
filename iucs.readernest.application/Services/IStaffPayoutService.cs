using iucs.readernest.application.Dto.Payouts;

namespace iucs.readernest.application.Services
{
    /// <summary>
    /// Non-teacher staff compensation: fixed monthly salary (Coordinators, Relationship
    /// Managers, Admin) or a percentage of monthly collection (Admission Counsellors). See
    /// StaffCompensationSetting and StaffPayout's own doc comments for the effective-dating and
    /// freeze-on-first-view rules this is built on.
    /// </summary>
    public interface IStaffPayoutService
    {
        /// <summary>Every Coordinator/Relationship Manager/Admission/Admin account Admin/Management can set compensation for, with whatever's currently configured (if any).</summary>
        Task<IReadOnlyList<EligibleStaffDto>> ListEligibleStaffAsync(CancellationToken cancellationToken = default);

        /// <summary>Sets or edits one staff member's compensation, effective from the given date (default today) onward.</summary>
        Task<StaffCompensationSettingDto> SetCompensationAsync(Guid userId, SaveStaffCompensationRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// The current month (live, recomputed from today's setting and today's collections
        /// every time) plus every earlier month back to when the staff member's compensation was
        /// first configured — each past month frozen the first time it's viewed here and never
        /// recomputed after. Used both for a staff member's own "My Earnings"/"My Salary" and an
        /// admin's per-person oversight view.
        /// </summary>
        Task<StaffEarningsDto> GetEarningsAsync(Guid userId, CancellationToken cancellationToken = default);

        /// <summary>Marks a frozen (past-month) payout as paid. Refused on the live current month, which has no row yet.</summary>
        Task<StaffPayoutDto> MarkPaidAsync(Guid staffPayoutId, CancellationToken cancellationToken = default);
    }
}
