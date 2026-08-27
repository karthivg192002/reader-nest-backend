using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Monitoring;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>Infra health for both production servers — Admin's Server Monitoring page.</summary>
    [ApiController]
    [Route("api/monitoring")]
    public class MonitoringController : ControllerBase
    {
        private readonly IMonitoringService _monitoringService;
        private readonly IServerLogService _serverLogService;

        public MonitoringController(IMonitoringService monitoringService, IServerLogService serverLogService)
        {
            _monitoringService = monitoringService;
            _serverLogService = serverLogService;
        }

        [HttpGet("summary")]
        [HasPermission(PermissionModule.SystemMonitoring, PermissionAction.View)]
        public async Task<ActionResult<MonitoringSummaryDto>> GetSummary(CancellationToken cancellationToken)
        {
            return Ok(await _monitoringService.GetSummaryAsync(cancellationToken));
        }

        /// <summary>Error-filtered `docker logs` tail for one container on one server. 400s for an unknown server/container or a server with no SSH configured, rather than a raw 500, so the frontend can show a clear reason instead of a generic failure.</summary>
        [HttpGet("servers/{serverName}/logs")]
        [HasPermission(PermissionModule.SystemMonitoring, PermissionAction.View)]
        public async Task<ActionResult<ServerLogsDto>> GetServerLogs(string serverName, [FromQuery] string container, [FromQuery] int lines, CancellationToken cancellationToken)
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
    }
}
