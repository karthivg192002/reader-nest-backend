using System.Security.Claims;
using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Academics;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    [ApiController]
    [Route("api/holidays")]
    public class HolidaysController : ControllerBase
    {
        private readonly IAcademicOpsService _academicOps;

        public HolidaysController(IAcademicOpsService academicOps)
        {
            _academicOps = academicOps;
        }

        [HttpGet]
        [Authorize]
        public async Task<ActionResult<IReadOnlyList<HolidayDto>>> List(CancellationToken cancellationToken)
        {
            return Ok(await _academicOps.ListHolidaysAsync(cancellationToken));
        }

        [HttpPost]
        [HasPermission(PermissionModule.SessionCalendarManagement, PermissionAction.Create)]
        public async Task<ActionResult<HolidayDto>> Create(SaveHolidayRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _academicOps.CreateHolidayAsync(request, cancellationToken));
        }

        [HttpDelete("{id:guid}")]
        [HasPermission(PermissionModule.SessionCalendarManagement, PermissionAction.Delete)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            await _academicOps.DeleteHolidayAsync(id, cancellationToken);
            return NoContent();
        }
    }

    [ApiController]
    [Route("api/leave-requests")]
    public class LeaveRequestsController : ControllerBase
    {
        private readonly IAcademicOpsService _academicOps;

        public LeaveRequestsController(IAcademicOpsService academicOps)
        {
            _academicOps = academicOps;
        }

        /// <summary>Teacher applies for leave; the 6-hour-before-session rule auto-blocks late requests.</summary>
        [HttpPost]
        [Authorize(Roles = nameof(UserRole.Teacher))]
        public async Task<ActionResult<LeaveRequestDto>> Submit(SubmitLeaveRequest request, CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _academicOps.SubmitLeaveAsync(userId, request, cancellationToken));
        }

        [HttpGet("mine")]
        [Authorize(Roles = nameof(UserRole.Teacher))]
        public async Task<ActionResult<IReadOnlyList<LeaveRequestDto>>> Mine(CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _academicOps.ListLeaveForTeacherUserAsync(userId, cancellationToken));
        }

        /// <summary>The signed-in teacher's own remaining class-wise-cancellation quota for
        /// the current calendar month — shown on the leave-application screen before she picks
        /// classes to cancel.</summary>
        [HttpGet("mine/allowance")]
        [Authorize(Roles = nameof(UserRole.Teacher))]
        public async Task<ActionResult<LeaveAllowanceStatusDto>> MyAllowance(CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _academicOps.GetMyLeaveAllowanceStatusAsync(userId, cancellationToken));
        }

        /// <summary>Teacher withdraws their own leave request while it's still Pending.</summary>
        [HttpDelete("{id:guid}")]
        [Authorize(Roles = nameof(UserRole.Teacher))]
        public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            await _academicOps.CancelLeaveAsync(userId, id, cancellationToken);
            return NoContent();
        }

        [HttpGet]
        [HasPermission(PermissionModule.LeaveManagement, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<LeaveRequestDto>>> List(
            [FromQuery] LeaveStatus? status,
            CancellationToken cancellationToken)
        {
            return Ok(await _academicOps.ListLeaveAsync(status, cancellationToken));
        }

        /// <summary>Admin approval/rejection; the teacher is notified either way.</summary>
        [HttpPost("{id:guid}/review")]
        [HasPermission(PermissionModule.LeaveManagement, PermissionAction.Approve)]
        public async Task<ActionResult<LeaveRequestDto>> Review(
            Guid id,
            ReviewLeaveRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _academicOps.ReviewLeaveAsync(id, request, cancellationToken));
        }
    }

    /// <summary>
    /// Admin-configured monthly class-wise-cancellation allowance (WBS Round 2 feedback #23:
    /// "teachers have a certain number of session cancellations allowed based on their total
    /// monthly sessions" — set per-teacher, or a centre-wide default, same shape as the
    /// Payroll rate cards). Reading the list is available to anyone who can review leave
    /// (so an RM approving a request can see the teacher's quota); setting it is Admin-only,
    /// same policy-level restriction Payroll rate cards already have.
    /// </summary>
    [ApiController]
    [Route("api/leave-allowances")]
    public class LeaveAllowancesController : ControllerBase
    {
        private readonly IAcademicOpsService _academicOps;

        public LeaveAllowancesController(IAcademicOpsService academicOps)
        {
            _academicOps = academicOps;
        }

        [HttpGet]
        [HasPermission(PermissionModule.LeaveManagement, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<LeaveAllowanceDto>>> List(CancellationToken cancellationToken)
        {
            return Ok(await _academicOps.ListLeaveAllowancesAsync(cancellationToken));
        }

        [HttpPost]
        [Authorize(Roles = nameof(UserRole.Admin))]
        public async Task<ActionResult<LeaveAllowanceDto>> Set(SaveLeaveAllowanceRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _academicOps.SetLeaveAllowanceAsync(request, cancellationToken));
        }
    }
}
