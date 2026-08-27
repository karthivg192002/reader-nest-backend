using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Common;
using iucs.readernest.application.Dto.Courses;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// Business departments (Phonics, Maths, and any admin-added ones) — previously a fixed
    /// 2-value enum, now a plain admin-managed list so a new subject area (Hindi, Abacus,
    /// Spoken English, ...) doesn't need a code change + redeploy to add. Each department
    /// still needs its own payment gateway account wired up separately under Payment Gateway
    /// Mapping before real invoices can route through it.
    /// </summary>
    [ApiController]
    [Route("api/departments")]
    public class DepartmentsController : ControllerBase
    {
        private readonly IDepartmentService _departmentService;

        public DepartmentsController(IDepartmentService departmentService)
        {
            _departmentService = departmentService;
        }

        [HttpGet]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<DepartmentDto>>> List(
            [FromQuery] bool includeInactive = false,
            CancellationToken cancellationToken = default)
        {
            return Ok(await _departmentService.ListAsync(includeInactive, cancellationToken));
        }

        [HttpPost]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.Create)]
        public async Task<ActionResult<DepartmentDto>> Create(
            SaveDepartmentRequest request,
            CancellationToken cancellationToken)
        {
            var department = await _departmentService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(List), null, department);
        }

        [HttpPut("{id:guid}")]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.Edit)]
        public async Task<ActionResult<DepartmentDto>> Update(
            Guid id,
            SaveDepartmentRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _departmentService.UpdateAsync(id, request, cancellationToken));
        }

        private const long MaxBulkImportBytes = 5 * 1024 * 1024;

        /// <summary>Bulk-create departments from an uploaded .csv/.xlsx. Columns: Name, Description, IsActive.</summary>
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
            return Ok(await _departmentService.BulkImportAsync(stream, file.FileName, cancellationToken));
        }

        [HttpGet("export")]
        [HasPermission(PermissionModule.CourseBatchManagement, PermissionAction.View)]
        public async Task<IActionResult> Export([FromQuery] bool includeInactive = true, CancellationToken cancellationToken = default)
        {
            var csv = await _departmentService.ExportCsvAsync(includeInactive, cancellationToken);
            return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", $"departments-{DateTime.UtcNow:yyyyMMdd}.csv");
        }
    }
}
