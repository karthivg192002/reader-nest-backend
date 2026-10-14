using System.Security.Claims;
using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Academics;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// Month-end leave and class-cancellation report per teacher (Admin Portal -> Leave
    /// Management). Open to anyone granted the Leave Management menu; regenerating the stored
    /// snapshot needs Edit on it.
    /// </summary>
    [ApiController]
    [Route("api/leave-cancellation-reports")]
    public class LeaveCancellationReportsController : ControllerBase
    {
        private readonly ILeaveCancellationReportService _reports;

        public LeaveCancellationReportsController(ILeaveCancellationReportService reports)
        {
            _reports = reports;
        }

        /// <summary>Months that already have a stored month-end snapshot, newest first.</summary>
        [HttpGet]
        [HasPermission(PermissionModule.LeaveManagement, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<LeaveCancellationReportMonthDto>>> ListGenerated(CancellationToken cancellationToken)
        {
            return Ok(await _reports.ListGeneratedAsync(cancellationToken));
        }

        /// <summary>The month's report: the frozen snapshot if generated, else a live preview.</summary>
        [HttpGet("{year:int}/{month:int}")]
        [HasPermission(PermissionModule.LeaveManagement, PermissionAction.View)]
        public async Task<ActionResult<LeaveCancellationReportDto>> Get(int year, int month, CancellationToken cancellationToken)
        {
            return Ok(await _reports.GetAsync(year, month, cancellationToken));
        }

        /// <summary>Recomputes the month and replaces its stored snapshot.</summary>
        [HttpPost("{year:int}/{month:int}/generate")]
        [HasPermission(PermissionModule.LeaveManagement, PermissionAction.Edit)]
        public async Task<ActionResult<LeaveCancellationReportDto>> Generate(int year, int month, CancellationToken cancellationToken)
        {
            Guid? userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
            return Ok(await _reports.GenerateAsync(year, month, userId, cancellationToken));
        }
    }
}
