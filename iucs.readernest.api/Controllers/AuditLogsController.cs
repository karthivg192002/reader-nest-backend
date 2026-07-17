using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Audit;
using iucs.readernest.application.Dto.Common;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>Read access to the audit trail for the admin / sub-admin Audit Log screen.</summary>
    [ApiController]
    [Route("api/audit-logs")]
    public class AuditLogsController : ControllerBase
    {
        private readonly IAuditLogService _auditLogService;

        public AuditLogsController(IAuditLogService auditLogService)
        {
            _auditLogService = auditLogService;
        }

        [HttpGet]
        [HasPermission(PermissionModule.Settings, PermissionAction.View)]
        public async Task<ActionResult<PagedResult<AuditLogDto>>> List(
            [FromQuery] string? entityName,
            [FromQuery] AuditAction? action,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25,
            CancellationToken cancellationToken = default)
        {
            return Ok(await _auditLogService.ListAsync(entityName, action, page, pageSize, cancellationToken));
        }
    }
}
