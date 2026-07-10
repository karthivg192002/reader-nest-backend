using System.Security.Claims;
using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Sessions;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    [ApiController]
    [Route("api/sessions")]
    public class SessionsController : ControllerBase
    {
        private readonly ISessionService _sessionService;

        public SessionsController(ISessionService sessionService)
        {
            _sessionService = sessionService;
        }

        [HttpGet]
        [HasPermission(PermissionModule.SessionCalendarManagement, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<ClassSessionDto>>> List(
            [FromQuery] DateTime fromUtc,
            [FromQuery] DateTime toUtc,
            [FromQuery] Guid? teacherProfileId,
            [FromQuery] Guid? batchId,
            CancellationToken cancellationToken)
        {
            return Ok(await _sessionService.ListAsync(fromUtc, toUtc, teacherProfileId, batchId, cancellationToken));
        }

        /// <summary>Teacher dashboard agenda: the caller's own sessions.</summary>
        [HttpGet("mine")]
        [Authorize(Roles = nameof(UserRole.Teacher))]
        public async Task<ActionResult<IReadOnlyList<ClassSessionDto>>> Mine(
            [FromQuery] DateTime fromUtc,
            [FromQuery] DateTime toUtc,
            CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _sessionService.ListForTeacherUserAsync(userId, fromUtc, toUtc, cancellationToken));
        }

        [HttpGet("{id:guid}")]
        [HasPermission(PermissionModule.SessionCalendarManagement, PermissionAction.View)]
        public async Task<ActionResult<ClassSessionDto>> Get(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _sessionService.GetAsync(id, cancellationToken));
        }

        [HttpPost]
        [HasPermission(PermissionModule.SessionCalendarManagement, PermissionAction.Create)]
        public async Task<ActionResult<ClassSessionDto>> Schedule(ScheduleSessionRequest request, CancellationToken cancellationToken)
        {
            var session = await _sessionService.ScheduleAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = session.Id }, session);
        }

        [HttpPost("{id:guid}/reschedule")]
        [HasPermission(PermissionModule.SessionCalendarManagement, PermissionAction.Edit)]
        public async Task<ActionResult<ClassSessionDto>> Reschedule(
            Guid id,
            RescheduleSessionRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _sessionService.RescheduleAsync(id, request, cancellationToken));
        }

        [HttpPost("{id:guid}/cancel")]
        [HasPermission(PermissionModule.SessionCalendarManagement, PermissionAction.Edit)]
        public async Task<ActionResult<ClassSessionDto>> Cancel(
            Guid id,
            CancelSessionRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _sessionService.CancelAsync(id, request, cancellationToken));
        }

        /// <summary>Marks a session completed; auto-moves the batch to Dormant when the course finishes.</summary>
        [HttpPost("{id:guid}/complete")]
        [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.Teacher)}")]
        public async Task<ActionResult<ClassSessionDto>> Complete(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _sessionService.CompleteAsync(id, cancellationToken));
        }
    }
}
