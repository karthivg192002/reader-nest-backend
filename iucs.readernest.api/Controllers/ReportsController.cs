using System.Security.Claims;
using System.Text;
using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Reports;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
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
            return Ok(await _reportsService.SendBulkEmailAsync(UserId(), request, cancellationToken));
        }

        /// <summary>Live recipient count for the compose screen (same rule as the send).</summary>
        [HttpGet("bulk-email/recipients")]
        [HasPermission(PermissionModule.Communication, PermissionAction.View)]
        public async Task<ActionResult<BulkEmailResultDto>> BulkEmailRecipients(
            [FromQuery] Guid? batchId,
            CancellationToken cancellationToken)
        {
            return Ok(await _reportsService.PreviewBulkEmailAsync(batchId, cancellationToken));
        }

        /// <summary>Past Bulk Email sends, newest first.</summary>
        [HttpGet("bulk-email/history")]
        [HasPermission(PermissionModule.Communication, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<BulkEmailHistoryItemDto>>> BulkEmailHistory(CancellationToken cancellationToken)
        {
            return Ok(await _reportsService.GetBulkEmailHistoryAsync(cancellationToken));
        }

        /// <summary>One blast's recipients, delivery status and replies.</summary>
        [HttpGet("bulk-email/history/{id:guid}")]
        [HasPermission(PermissionModule.Communication, PermissionAction.View)]
        public async Task<ActionResult<BulkEmailBlastDetailDto>> BulkEmailHistoryDetail(Guid id, CancellationToken cancellationToken)
        {
            return Ok(await _reportsService.GetBulkEmailBlastDetailAsync(id, cancellationToken));
        }

        /// <summary>A parent's reply to one Bulk Email they received.</summary>
        [HttpPost("bulk-email/{recipientId:guid}/reply")]
        [Authorize(Roles = nameof(UserRole.Parent))]
        public async Task<IActionResult> ReplyToBulkEmail(
            Guid recipientId,
            ReplyToBulkEmailRequest request,
            CancellationToken cancellationToken)
        {
            await _reportsService.ReplyToBulkEmailAsync(UserId(), recipientId, request, cancellationToken);
            return NoContent();
        }

        private Guid UserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    }
}
