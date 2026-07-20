using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Batches;
using iucs.readernest.application.Dto.Sessions;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    [ApiController]
    [Route("api/batches")]
    public class BatchesController : ControllerBase
    {
        private readonly IBatchService _batchService;
        private readonly ISessionService _sessionService;

        public BatchesController(IBatchService batchService, ISessionService sessionService)
        {
            _batchService = batchService;
            _sessionService = sessionService;
        }

        [HttpGet]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<BatchDto>>> List(
            [FromQuery] BatchStatus? status,
            CancellationToken cancellationToken)
        {
            return Ok(await _batchService.ListAsync(status, cancellationToken));
        }

        [HttpGet("{id:guid}")]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.View)]
        public async Task<ActionResult<BatchDto>> Get(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _batchService.GetAsync(id, cancellationToken));
        }

        [HttpPost]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.Create)]
        public async Task<ActionResult<BatchDto>> Create(SaveBatchRequest request, CancellationToken cancellationToken)
        {
            var batch = await _batchService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = batch.Id }, batch);
        }

        [HttpPut("{id:guid}")]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.Edit)]
        public async Task<ActionResult<BatchDto>> Update(Guid id, SaveBatchRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _batchService.UpdateAsync(id, request, cancellationToken));
        }

        /// <summary>Automated scheduling: places every course session on the chosen weekdays, skipping holidays.</summary>
        [HttpPost("{id:guid}/generate-schedule")]
        [HasPermission(PermissionModule.SessionCalendarManagement, PermissionAction.Create)]
        public async Task<ActionResult<IReadOnlyList<ClassSessionDto>>> GenerateSchedule(
            Guid id,
            GenerateScheduleRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _sessionService.GenerateScheduleAsync(id, request, cancellationToken));
        }

        [HttpPut("{id:guid}/status")]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.Edit)]
        public async Task<ActionResult<BatchDto>> SetStatus(
            Guid id,
            UpdateBatchStatusRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _batchService.SetStatusAsync(id, request.Status, cancellationToken));
        }

        /// <summary>The batch's current student roster (WBS p.17 "Assign Students").</summary>
        [HttpGet("{id:guid}/enrollments")]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<BatchStudentDto>>> ListEnrollments(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _batchService.ListEnrollmentsAsync(id, cancellationToken));
        }

        /// <summary>Active, approved children not yet in this batch — candidates for the assign picker.</summary>
        [HttpGet("{id:guid}/unassigned-students")]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<UnassignedChildDto>>> ListUnassigned(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _batchService.ListUnassignedStudentsAsync(id, cancellationToken));
        }

        /// <summary>Places a child in the batch (rejected once the batch is at capacity).</summary>
        [HttpPost("{id:guid}/enrollments")]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.Edit)]
        public async Task<ActionResult<BatchStudentDto>> AssignStudent(
            Guid id,
            AssignStudentRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _batchService.AssignStudentAsync(id, request.ChildId, cancellationToken));
        }

        /// <summary>Withdraws a child from the batch, freeing a seat.</summary>
        [HttpDelete("{id:guid}/enrollments/{childId:guid}")]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.Edit)]
        public async Task<IActionResult> RemoveStudent(Guid id, Guid childId, CancellationToken cancellationToken)
        {
            await _batchService.RemoveStudentAsync(id, childId, cancellationToken);
            return NoContent();
        }
    }
}
