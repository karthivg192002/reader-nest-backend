using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Common;
using iucs.readernest.application.Dto.Sessions;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// Read access to the durable class-meeting activity trail (Class Session Logs) — every
    /// teacher start/join/leave/end, student join/leave, no-show and denied join attempt,
    /// across demo and regular classes alike. Admin passes implicitly; a Sub Admin (e.g. an
    /// IT Admin persona) needs the ClassSessionLogs module granted via Roles &amp; Permissions.
    /// </summary>
    [ApiController]
    [Route("api/class-session-logs")]
    [Authorize(Roles = $"{nameof(UserRole.Admin)},{nameof(UserRole.SubAdmin)}")]
    [HasPermission(PermissionModule.ClassSessionLogs, PermissionAction.View)]
    public class ClassSessionLogsController : ControllerBase
    {
        private readonly IClassSessionLogService _classSessionLogService;

        public ClassSessionLogsController(IClassSessionLogService classSessionLogService)
        {
            _classSessionLogService = classSessionLogService;
        }

        [HttpGet("dashboard")]
        public async Task<ActionResult<ClassSessionLogDashboardDto>> Dashboard(CancellationToken cancellationToken)
        {
            return Ok(await _classSessionLogService.GetDashboardAsync(cancellationToken));
        }

        [HttpGet]
        public async Task<ActionResult<PagedResult<ClassSessionEventLogDto>>> List(
            [FromQuery] DateTime? fromUtc,
            [FromQuery] DateTime? toUtc,
            [FromQuery] Guid? sessionId,
            [FromQuery] Guid? teacherProfileId,
            [FromQuery] ClassSessionEventType? eventType,
            [FromQuery] bool? expectedOnly,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25,
            CancellationToken cancellationToken = default)
        {
            return Ok(await _classSessionLogService.ListEventLogsAsync(
                AsUtc(fromUtc), AsUtc(toUtc), sessionId, teacherProfileId, eventType, expectedOnly, page, pageSize, cancellationToken));
        }

        [HttpGet("session/{sessionId:guid}")]
        public async Task<ActionResult<IReadOnlyList<ClassSessionEventLogDto>>> Timeline(
            Guid sessionId, CancellationToken cancellationToken)
        {
            return Ok(await _classSessionLogService.GetSessionTimelineAsync(sessionId, cancellationToken));
        }

        // Model binding parses a bare date/no-offset query value as Kind=Unspecified, which
        // Npgsql then refuses to compare against a timestamptz column — same guard as
        // SessionsController.AsUtc, extended to the nullable case both filters here use.
        private static DateTime? AsUtc(DateTime? value) =>
            value is null ? null : value.Value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);
    }
}
