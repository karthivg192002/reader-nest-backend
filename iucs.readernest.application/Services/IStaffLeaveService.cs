using iucs.readernest.application.Dto.Users;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Services
{
    /// <summary>
    /// Leave for admin-team members (RM, Coordinator, Management, Admission): apply at any time
    /// with no minimum notice; Admin / Founder approve or reject.
    /// </summary>
    public interface IStaffLeaveService
    {
        Task<IReadOnlyList<StaffLeaveRequestDto>> ListMineAsync(Guid userId, CancellationToken cancellationToken = default);

        /// <summary>Applies for leave and alerts the approvers. No minimum notice; overlapping pending/approved leave is refused.</summary>
        Task<StaffLeaveRequestDto> SubmitAsync(Guid userId, SubmitStaffLeaveRequest request, CancellationToken cancellationToken = default);

        /// <summary>Withdraws the caller's own leave while it is still Pending.</summary>
        Task<StaffLeaveRequestDto> CancelMineAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<StaffLeaveRequestDto>> ListAsync(LeaveStatus? status, CancellationToken cancellationToken = default);

        /// <summary>Approves or rejects a Pending request and emails the staff member.</summary>
        Task<StaffLeaveRequestDto> ReviewAsync(Guid reviewerUserId, Guid id, ReviewStaffLeaveRequest request, CancellationToken cancellationToken = default);
    }
}
