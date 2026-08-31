using System.Security.Claims;
using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Activities;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// Admin-authored whiteboard activity bank, scoped by department and (optionally) course —
    /// replaces the single hardcoded fruit/letter matching set (and fixed hotspot game) every
    /// live class used to share regardless of subject. Content management is Admin/Sub-Admin
    /// (CourseBatchManagement); reading the resolved set for one class is open to any genuine
    /// session participant, same pattern as <see cref="QuizQuestionsController"/>.
    /// </summary>
    [ApiController]
    [Route("api/whiteboard-activities")]
    public class WhiteboardActivitiesController : ControllerBase
    {
        private readonly IWhiteboardActivityService _whiteboardActivityService;

        public WhiteboardActivitiesController(IWhiteboardActivityService whiteboardActivityService)
        {
            _whiteboardActivityService = whiteboardActivityService;
        }

        [HttpGet]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<WhiteboardActivityDto>>> List(
            [FromQuery] Guid? departmentId,
            [FromQuery] Guid? courseId,
            CancellationToken cancellationToken)
        {
            return Ok(await _whiteboardActivityService.ListAsync(departmentId, courseId, cancellationToken));
        }

        [HttpPost]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.Create)]
        public async Task<ActionResult<WhiteboardActivityDto>> Create(
            SaveWhiteboardActivityRequest request,
            CancellationToken cancellationToken)
        {
            var activity = await _whiteboardActivityService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(List), null, activity);
        }

        [HttpPut("{id:guid}")]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.Edit)]
        public async Task<ActionResult<WhiteboardActivityDto>> Update(
            Guid id,
            SaveWhiteboardActivityRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _whiteboardActivityService.UpdateAsync(id, request, cancellationToken));
        }

        [HttpDelete("{id:guid}")]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.Delete)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            await _whiteboardActivityService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }

        /// <summary>Resolved activity set for one live class — this is what the classroom now
        /// launches from instead of a hardcoded bank.</summary>
        [HttpGet("for-session/{sessionId:guid}")]
        [Authorize]
        public async Task<ActionResult<IReadOnlyList<WhiteboardActivityDto>>> ForSession(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _whiteboardActivityService.GetForSessionAsync(sessionId, userId, cancellationToken));
        }
    }
}
