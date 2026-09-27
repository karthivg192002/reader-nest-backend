using System.Security.Claims;
using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Users;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// Admin-team leave: RMs, Coordinators, Management and Admission apply ("mine" routes, no
    /// minimum notice); Admin / Founder (UserManagement:Approve) review. Teachers keep their own
    /// class-aware leave under /api/leave.
    /// </summary>
    [ApiController]
    [Route("api/staff-leave")]
    public class StaffLeaveController : ControllerBase
    {
        private const string StaffRoles = $"{nameof(UserRole.SubAdmin)},{nameof(UserRole.AdmissionTeam)}";

        private readonly IStaffLeaveService _staffLeave;

        public StaffLeaveController(IStaffLeaveService staffLeave)
        {
            _staffLeave = staffLeave;
        }

        [HttpGet("mine")]
        [Authorize(Roles = StaffRoles)]
        public async Task<ActionResult<IReadOnlyList<StaffLeaveRequestDto>>> ListMine(CancellationToken cancellationToken)
        {
            return Ok(await _staffLeave.ListMineAsync(UserId(), cancellationToken));
        }

        [HttpPost("mine")]
        [Authorize(Roles = StaffRoles)]
        public async Task<ActionResult<StaffLeaveRequestDto>> Submit(SubmitStaffLeaveRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _staffLeave.SubmitAsync(UserId(), request, cancellationToken));
        }

        [HttpPost("mine/{id:guid}/cancel")]
        [Authorize(Roles = StaffRoles)]
        public async Task<ActionResult<StaffLeaveRequestDto>> CancelMine(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _staffLeave.CancelMineAsync(UserId(), id, cancellationToken));
        }

        [HttpGet]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Approve)]
        public async Task<ActionResult<IReadOnlyList<StaffLeaveRequestDto>>> List(
            [FromQuery] LeaveStatus? status, CancellationToken cancellationToken)
        {
            return Ok(await _staffLeave.ListAsync(status, cancellationToken));
        }

        [HttpPost("{id:guid}/review")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Approve)]
        public async Task<ActionResult<StaffLeaveRequestDto>> Review(
            Guid id, ReviewStaffLeaveRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _staffLeave.ReviewAsync(UserId(), id, request, cancellationToken));
        }

        private Guid UserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }
}
