using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Billing;
using iucs.readernest.application.Dto.Common;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    // Parent also carries BillingFinance:View for their own /parent/billing screen
    // (served separately by ParentPortalController) — without a role restriction that
    // same claim reaches this unscoped, admin-only plan-configuration screen too.
    [ApiController]
    [Route("api/package-plans")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)}")]
    public class PackagePlansController : ControllerBase
    {
        private readonly IBillingService _billingService;

        public PackagePlansController(IBillingService billingService)
        {
            _billingService = billingService;
        }

        [HttpGet]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<PackagePlanDto>>> List(CancellationToken cancellationToken)
        {
            return Ok(await _billingService.ListPlansAsync(cancellationToken));
        }

        [HttpPost]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.Create)]
        public async Task<ActionResult<PackagePlanDto>> Create(SavePackagePlanRequest request, CancellationToken cancellationToken)
        {
            var plan = await _billingService.CreatePlanAsync(request, cancellationToken);
            return CreatedAtAction(nameof(List), null, plan);
        }

        [HttpPut("{id:guid}")]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.Edit)]
        public async Task<ActionResult<PackagePlanDto>> Update(
            Guid id,
            SavePackagePlanRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _billingService.UpdatePlanAsync(id, request, cancellationToken));
        }

        private const long MaxBulkImportBytes = 5 * 1024 * 1024;

        /// <summary>Columns: Name, CourseName (optional), BillingType, BillingCycle, Price, SessionsIncluded (optional), IsActive.</summary>
        [HttpPost("bulk-import")]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.Create)]
        [RequestSizeLimit(MaxBulkImportBytes)]
        public async Task<ActionResult<BulkImportResult>> BulkImport(IFormFile file, CancellationToken cancellationToken)
        {
            if (file.Length == 0)
            {
                return BadRequest("The uploaded file is empty.");
            }

            await using var stream = file.OpenReadStream();
            return Ok(await _billingService.BulkImportPlansAsync(stream, file.FileName, cancellationToken));
        }

        [HttpGet("export")]
        [HasPermission(PermissionModule.BillingFinance, PermissionAction.View)]
        public async Task<IActionResult> Export(CancellationToken cancellationToken)
        {
            var csv = await _billingService.ExportPlansCsvAsync(cancellationToken);
            return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"package-plans-{DateTime.UtcNow:yyyyMMdd}.csv");
        }
    }
}
