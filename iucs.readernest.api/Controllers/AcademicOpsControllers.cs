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
}
