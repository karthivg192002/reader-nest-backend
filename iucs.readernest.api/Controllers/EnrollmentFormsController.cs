using System.Security.Claims;
using System.Text;
using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Enrollment;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    [ApiController]
    [Route("api/enrollment-forms")]
    public class EnrollmentFormsController : ControllerBase
    {
        private readonly IEnrollmentService _enrollmentService;

        public EnrollmentFormsController(IEnrollmentService enrollmentService)
        {
            _enrollmentService = enrollmentService;
        }

        /// <summary>Mandatory first-login enrollment form submission by the parent.</summary>
        [HttpPost]
        [Authorize(Roles = nameof(UserRole.Parent))]
        public async Task<ActionResult<EnrollmentFormDto>> Submit(
            SubmitEnrollmentFormRequest request,
            CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _enrollmentService.SubmitAsync(userId, request, cancellationToken));
        }

        /// <summary>The parent's own submissions and their review state.</summary>
        [HttpGet("mine")]
        [Authorize(Roles = nameof(UserRole.Parent))]
        public async Task<ActionResult<IReadOnlyList<EnrollmentFormDto>>> Mine(CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            return Ok(await _enrollmentService.ListForParentUserAsync(userId, cancellationToken));
        }

        [HttpGet]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<EnrollmentFormDto>>> List(
            [FromQuery] EnrollmentFormStatus? status,
            CancellationToken cancellationToken)
        {
            return Ok(await _enrollmentService.ListAsync(status, cancellationToken));
        }

        /// <summary>Approval creates the Child record and unlocks the parent dashboard.</summary>
        [HttpPost("{id:guid}/review")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Approve)]
        public async Task<ActionResult<EnrollmentFormDto>> Review(
            Guid id,
            ReviewEnrollmentFormRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _enrollmentService.ReviewAsync(id, request, cancellationToken));
        }

        /// <summary>Admin download of the submitted form as a JSON document.</summary>
        [HttpGet("{id:guid}/download")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.View)]
        public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
        {
            var form = await _enrollmentService.GetAsync(id, cancellationToken);
            return File(Encoding.UTF8.GetBytes(form.FormDataJson), "application/json", $"enrollment-{form.Id}.json");
        }
    }
}
