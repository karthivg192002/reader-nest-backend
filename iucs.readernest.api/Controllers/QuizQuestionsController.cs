using System.Security.Claims;
using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Common;
using iucs.readernest.application.Dto.Quizzes;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// Admin-authored live-quiz question bank, scoped by department and (optionally) course —
    /// replaces the single hardcoded question set every class used to share regardless of
    /// subject. Content management is Admin/Sub-Admin (CourseBatchManagement); reading the
    /// resolved set for one class is open to any genuine session participant (teacher, the
    /// session's own parents, Admin, a coordinator) via the same ownership check every other
    /// session-scoped endpoint uses.
    /// </summary>
    [ApiController]
    [Route("api/quiz-questions")]
    public class QuizQuestionsController : ControllerBase
    {
        private readonly IQuizQuestionService _quizQuestionService;

        public QuizQuestionsController(IQuizQuestionService quizQuestionService)
        {
            _quizQuestionService = quizQuestionService;
        }

        [HttpGet]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<QuizQuestionDto>>> List(
            [FromQuery] Guid? departmentId,
            [FromQuery] Guid? courseId,
            CancellationToken cancellationToken)
        {
            return Ok(await _quizQuestionService.ListAsync(departmentId, courseId, cancellationToken));
        }

        [HttpPost]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.Create)]
        public async Task<ActionResult<QuizQuestionDto>> Create(
            SaveQuizQuestionRequest request,
            CancellationToken cancellationToken)
        {
            var question = await _quizQuestionService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(List), null, question);
        }

        [HttpPut("{id:guid}")]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.Edit)]
        public async Task<ActionResult<QuizQuestionDto>> Update(
            Guid id,
            SaveQuizQuestionRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _quizQuestionService.UpdateAsync(id, request, cancellationToken));
        }

        [HttpDelete("{id:guid}")]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.Delete)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            await _quizQuestionService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }

        /// <summary>Resolved quiz set for one live class — this is what the classroom now
        /// launches from instead of a hardcoded bank.</summary>
        [HttpGet("for-session/{sessionId:guid}")]
        [Authorize]
        public async Task<ActionResult<IReadOnlyList<QuizQuestionDto>>> ForSession(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _quizQuestionService.GetForSessionAsync(sessionId, userId, cancellationToken));
        }

        private const long MaxBulkImportBytes = 5 * 1024 * 1024;

        /// <summary>Columns: DepartmentName (required unless CourseName set), CourseName, Prompt, Option1..Option6, CorrectOptionNumber.</summary>
        [HttpPost("bulk-import")]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.Create)]
        [RequestSizeLimit(MaxBulkImportBytes)]
        public async Task<ActionResult<BulkImportResult>> BulkImport(IFormFile file, CancellationToken cancellationToken)
        {
            if (file.Length == 0)
            {
                return BadRequest("The uploaded file is empty.");
            }

            await using var stream = file.OpenReadStream();
            return Ok(await _quizQuestionService.BulkImportAsync(stream, file.FileName, cancellationToken));
        }

        [HttpGet("export")]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.View)]
        public async Task<IActionResult> Export(
            [FromQuery] Guid? departmentId, [FromQuery] Guid? courseId, CancellationToken cancellationToken)
        {
            var csv = await _quizQuestionService.ExportCsvAsync(departmentId, courseId, cancellationToken);
            return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"quiz-questions-{DateTime.UtcNow:yyyyMMdd}.csv");
        }
    }
}
