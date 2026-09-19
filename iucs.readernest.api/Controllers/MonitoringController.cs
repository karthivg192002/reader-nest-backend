using System.Security.Claims;
using iucs.readernest.api.Auth;
using iucs.readernest.api.Hubs;
using iucs.readernest.application.Dto.Monitoring;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;

namespace iucs.readernest.api.Controllers
{
    /// <summary>Infra health for both production servers — Admin's Server Monitoring page.</summary>
    [ApiController]
    [Route("api/monitoring")]
    public class MonitoringController : ControllerBase
    {
        private readonly IMonitoringService _monitoringService;
        private readonly IServerLogService _serverLogService;
        private readonly IServerControlService _serverControlService;
        private readonly IBurstWorkerControlService _burstWorkerControlService;
        private readonly IHubContext<ClassroomHub> _classroomHub;

        public MonitoringController(
            IMonitoringService monitoringService,
            IServerLogService serverLogService,
            IServerControlService serverControlService,
            IBurstWorkerControlService burstWorkerControlService,
            IHubContext<ClassroomHub> classroomHub)
        {
            _monitoringService = monitoringService;
            _serverLogService = serverLogService;
            _serverControlService = serverControlService;
            _burstWorkerControlService = burstWorkerControlService;
            _classroomHub = classroomHub;
        }

        [HttpGet("summary")]
        [HasPermission(PermissionModule.SystemMonitoring, PermissionAction.View)]
        public async Task<ActionResult<MonitoringSummaryDto>> GetSummary(CancellationToken cancellationToken)
        {
            return Ok(await _monitoringService.GetSummaryAsync(cancellationToken));
        }

        /// <summary>Every live class session with a connected participant right now, and who's in it -- admin-only visibility into other users' real-time presence.</summary>
        [HttpGet("live-users")]
        [HasPermission(PermissionModule.SystemMonitoring, PermissionAction.View)]
        public async Task<ActionResult<List<LiveClassSessionDto>>> GetLiveUsers(CancellationToken cancellationToken)
        {
            return Ok(await _monitoringService.GetLiveUsersAsync(cancellationToken));
        }

        /// <summary>Every session scheduled today (IST), live or not, with attendance counts.</summary>
        [HttpGet("today-sessions")]
        [HasPermission(PermissionModule.SystemMonitoring, PermissionAction.View)]
        public async Task<ActionResult<List<SessionHistoryEntryDto>>> GetTodaySessions(CancellationToken cancellationToken)
        {
            return Ok(await _monitoringService.GetTodaySessionsAsync(cancellationToken));
        }

        /// <summary>
        /// CPU/memory history for one server over an admin-selected window ("1h", "24h", or "7d").
        /// serverName is a query param, not a path segment: both real server names ("Jitsi / Video",
        /// "App / API") contain "/", which ASP.NET Core's routing never decodes from %2F in a route
        /// value even when the client percent-encodes it correctly -- a path segment binds to
        /// "Jitsi %2F Video" verbatim and every one of these lookups 400s. Confirmed broken in
        /// production this way for all five serverName-keyed endpoints below.
        /// </summary>
        [HttpGet("servers/history")]
        [HasPermission(PermissionModule.SystemMonitoring, PermissionAction.View)]
        public async Task<ActionResult<HistoryRangeDto>> GetHistory([FromQuery] string serverName, [FromQuery] string range, CancellationToken cancellationToken)
        {
            try
            {
                return Ok(await _monitoringService.GetHistoryAsync(serverName, string.IsNullOrWhiteSpace(range) ? "1h" : range, cancellationToken));
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>Error-filtered `docker logs` tail for one container on one server. 400s for an unknown server/container or a server with no SSH configured, rather than a raw 500, so the frontend can show a clear reason instead of a generic failure.</summary>
        [HttpGet("servers/logs")]
        [HasPermission(PermissionModule.SystemMonitoring, PermissionAction.View)]
        public async Task<ActionResult<ServerLogsDto>> GetServerLogs([FromQuery] string serverName, [FromQuery] string container, [FromQuery] int lines, CancellationToken cancellationToken)
        {
            try
            {
                return Ok(await _serverLogService.GetContainerErrorLogsAsync(serverName, container, lines <= 0 ? 300 : lines, cancellationToken));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Tells every browser currently connected to a live class to show a dismissible
        /// "an update is available" prompt, so a deploy actually reaches people already
        /// mid-session without waiting for their next natural page load. Deliberately a
        /// gentle, dismissible notification -- not a forced reload -- since an unannounced
        /// refresh would drop a teacher/student straight out of an active Jitsi call
        /// (camera, mic, recording continuity, everything) with no warning. Only reaches
        /// ClassroomHub's own connections (live classes), not every open tab app-wide --
        /// there's no such app-wide channel today, and this is the case that actually
        /// matters: someone sitting on a static page with no live real-time connection
        /// picks up a new deploy on their next navigation anyway.
        /// </summary>
        [HttpPost("broadcast-reload")]
        [HasPermission(PermissionModule.SystemMonitoring, PermissionAction.Edit)]
        public async Task<ActionResult> BroadcastReload([FromQuery] string? message, CancellationToken cancellationToken)
        {
            await _classroomHub.Clients.All.SendAsync(
                "ReloadRequested",
                string.IsNullOrWhiteSpace(message) ? "An update is available." : message,
                cancellationToken);
            return Ok();
        }

        /// <summary>Runs the Jibri autoscaler immediately instead of waiting out its next cron minute.</summary>
        [HttpPost("servers/jibri/rescale")]
        [HasPermission(PermissionModule.SystemMonitoring, PermissionAction.Edit)]
        public async Task<ActionResult<JibriControlResultDto>> RescaleJibriNow([FromQuery] string serverName, CancellationToken cancellationToken)
        {
            try
            {
                return Ok(await _serverControlService.RescaleJibriNowAsync(serverName, cancellationToken));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>Sets how many Jibri instances stay warm even while idle.</summary>
        [HttpPut("servers/jibri/min-replicas")]
        [HasPermission(PermissionModule.SystemMonitoring, PermissionAction.Edit)]
        public async Task<ActionResult<JibriControlResultDto>> SetJibriMinReplicas([FromQuery] string serverName, [FromBody] SetJibriMinReplicasRequest request, CancellationToken cancellationToken)
        {
            try
            {
                return Ok(await _serverControlService.SetJibriMinReplicasAsync(serverName, request.MinReplicas, cancellationToken));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        /// <summary>
        /// Emergency manual start of the on-demand burst recording worker (billed hourly while it
        /// exists). Holds it up for <paramref name="holdMinutes"/> so it isn't deleted while idle.
        /// </summary>
        [HttpPost("burst-worker/start")]
        [HasPermission(PermissionModule.SystemMonitoring, PermissionAction.Edit)]
        public async Task<ActionResult<BurstWorkerControlResultDto>> StartBurstWorker([FromQuery] int holdMinutes = 120, CancellationToken cancellationToken = default)
        {
            var requestedBy = User.FindFirst(ClaimTypes.Email)?.Value
                ?? User.FindFirst("email")?.Value
                ?? User.Identity?.Name
                ?? "admin";
            try
            {
                return Ok(await _burstWorkerControlService.StartAsync(holdMinutes, requestedBy, cancellationToken));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { message = "Couldn't reach the Jitsi server to start the burst worker: " + ex.Message });
            }
        }

        /// <summary>Stops the burst worker as soon as it is safe: every recorder idle and every recording fully on main. Never force-deletes.</summary>
        [HttpPost("burst-worker/stop")]
        [HasPermission(PermissionModule.SystemMonitoring, PermissionAction.Edit)]
        public async Task<ActionResult<BurstWorkerControlResultDto>> StopBurstWorker(CancellationToken cancellationToken = default)
        {
            try
            {
                return Ok(await _burstWorkerControlService.StopAsync(cancellationToken));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return StatusCode(StatusCodes.Status502BadGateway, new { message = "Couldn't reach the Jitsi server to stop the burst worker: " + ex.Message });
            }
        }

        /// <summary>Restarts one configured container on one server -- interrupts anything currently using that service.</summary>
        [HttpPost("servers/services/restart")]
        [HasPermission(PermissionModule.SystemMonitoring, PermissionAction.Edit)]
        public async Task<ActionResult<ServiceRestartResultDto>> RestartService([FromQuery] string serverName, [FromQuery] string container, CancellationToken cancellationToken)
        {
            try
            {
                return Ok(await _serverControlService.RestartContainerAsync(serverName, container, cancellationToken));
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
    }
}
