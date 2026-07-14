using System.Security.Claims;
using iucs.readernest.application.Dto.Billing;
using iucs.readernest.application.Dto.Enrollment;
using iucs.readernest.application.Dto.Portal;
using iucs.readernest.application.Dto.Resources;
using iucs.readernest.application.Dto.Sessions;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>Everything the signed-in parent's unified dashboard needs.</summary>
    [ApiController]
    [Route("api/parent-portal")]
    [Authorize(Roles = nameof(UserRole.Parent))]
    public class ParentPortalController : ControllerBase
    {
        private readonly IParentPortalService _parentPortal;
        private readonly IEnrollmentService _enrollmentService;
        private readonly IIntegrationService _integrationService;

        public ParentPortalController(
            IParentPortalService parentPortal,
            IEnrollmentService enrollmentService,
            IIntegrationService integrationService)
        {
            _parentPortal = parentPortal;
            _enrollmentService = enrollmentService;
            _integrationService = integrationService;
        }

        [HttpGet("dashboard")]
        public async Task<ActionResult<ParentDashboardDto>> Dashboard(CancellationToken cancellationToken)
        {
            return Ok(await _parentPortal.GetDashboardAsync(UserId(), cancellationToken));
        }

        [HttpGet("children")]
        public async Task<ActionResult<IReadOnlyList<ChildDto>>> Children(CancellationToken cancellationToken)
        {
            return Ok(await _enrollmentService.ListChildrenForParentUserAsync(UserId(), cancellationToken));
        }

        [HttpGet("schedule")]
        public async Task<ActionResult<IReadOnlyList<ClassSessionDto>>> Schedule(
            [FromQuery] DateTime fromUtc,
            [FromQuery] DateTime toUtc,
            CancellationToken cancellationToken)
        {
            return Ok(await _parentPortal.GetScheduleAsync(UserId(), fromUtc, toUtc, cancellationToken));
        }

        /// <summary>Granted resources; returns 400 with the Pay Now message while fee-suspended.</summary>
        [HttpGet("resources")]
        public async Task<ActionResult<IReadOnlyList<ResourceDto>>> Resources(CancellationToken cancellationToken)
        {
            return Ok(await _parentPortal.GetResourcesAsync(UserId(), cancellationToken));
        }

        [HttpGet("invoices")]
        public async Task<ActionResult<IReadOnlyList<InvoiceDto>>> Invoices(CancellationToken cancellationToken)
        {
            return Ok(await _parentPortal.GetInvoicesAsync(UserId(), cancellationToken));
        }

        /// <summary>Enabled payment gateways for the Pay Now popup; the UI adds a Cash option on top.</summary>
        [HttpGet("payment-methods")]
        public async Task<ActionResult<IReadOnlyList<PaymentMethodOptionDto>>> PaymentMethods(CancellationToken cancellationToken)
        {
            return Ok(await _integrationService.GetEnabledPaymentMethodsAsync(cancellationToken));
        }

        /// <summary>Grant-checked worksheet download (books stay view-only).</summary>
        [HttpGet("resources/{id:guid}/download")]
        public async Task<IActionResult> DownloadResource(
            Guid id,
            [FromServices] IResourceService resourceService,
            [FromServices] application.Common.Interfaces.IFileStorage fileStorage,
            CancellationToken cancellationToken)
        {
            await _parentPortal.GetResourceForDownloadAsync(UserId(), id, cancellationToken);

            var resource = await resourceService.GetForDownloadAsync(id, cancellationToken);
            var absolutePath = fileStorage.GetAbsolutePath(resource.FileUrl);
            if (!System.IO.File.Exists(absolutePath))
            {
                return NotFound();
            }

            var mimeType = string.IsNullOrWhiteSpace(resource.MimeType) ? "application/octet-stream" : resource.MimeType;
            return PhysicalFile(absolutePath, mimeType, $"{resource.Title}{Path.GetExtension(resource.FileUrl)}");
        }

        private Guid UserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }
}
