using System.Text;
using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Reports;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    [ApiController]
    [Route("api/reports")]
    public class ReportsController : ControllerBase
    {
        private readonly IReportsService _reportsService;

        public ReportsController(IReportsService reportsService)
        {
            _reportsService = reportsService;
        }

        /// <summary>Admin/Management BI dashboard aggregates.</summary>
        [HttpGet("dashboard-summary")]
        [HasPermission(PermissionModule.ReportsAnalytics, PermissionAction.View)]
        public async Task<ActionResult<DashboardSummaryDto>> DashboardSummary(CancellationToken cancellationToken)
        {
            return Ok(await _reportsService.GetDashboardSummaryAsync(cancellationToken));
        }

        /// <summary>CSV export: attendance | revenue | payouts | conversion.</summary>
        [HttpGet("export/{report}")]
        [HasPermission(PermissionModule.ReportsAnalytics, PermissionAction.View)]
        public async Task<IActionResult> Export(string report, CancellationToken cancellationToken)
        {
            var csv = await _reportsService.ExportCsvAsync(report, cancellationToken);
            return File(Encoding.UTF8.GetBytes(csv), "text/csv", $"{report}-{DateTime.UtcNow:yyyyMMdd}.csv");
        }

        /// <summary>Teacher performance view: delivery, no-shows, attendance, summaries.</summary>
        [HttpGet("teacher-performance")]
        [HasPermission(PermissionModule.ReportsAnalytics, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<TeacherPerformanceDto>>> TeacherPerformance(CancellationToken cancellationToken)
        {
            return Ok(await _reportsService.GetTeacherPerformanceAsync(cancellationToken));
        }

        /// <summary>Student analytics + generated progress insights.</summary>
        [HttpGet("student-analytics/{childId:guid}")]
        [HasPermission(PermissionModule.ReportsAnalytics, PermissionAction.View)]
        public async Task<ActionResult<StudentAnalyticsDto>> StudentAnalytics(Guid childId, CancellationToken cancellationToken)
        {
            return Ok(await _reportsService.GetStudentAnalyticsAsync(childId, cancellationToken));
        }
    }

    [ApiController]
    [Route("api/communications")]
    public class CommunicationsController : ControllerBase
    {
        private readonly IReportsService _reportsService;

        public CommunicationsController(IReportsService reportsService)
        {
            _reportsService = reportsService;
        }

        /// <summary>Bulk email to all active parents, or scoped to one batch.</summary>
        [HttpPost("bulk-email")]
        [HasPermission(PermissionModule.Communication, PermissionAction.Create)]
        public async Task<ActionResult<BulkEmailResultDto>> BulkEmail(
            BulkEmailRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _reportsService.SendBulkEmailAsync(request, cancellationToken));
        }
    }
}
