using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Settings;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    [ApiController]
    [Route("api/settings")]
    public class SettingsController : ControllerBase
    {
        private readonly ISettingsService _settingsService;

        public SettingsController(ISettingsService settingsService)
        {
            _settingsService = settingsService;
        }

        /// <summary>Branding and other public settings; anonymous so the login screen can brand itself.</summary>
        [HttpGet("public")]
        [AllowAnonymous]
        public async Task<ActionResult<IReadOnlyList<SettingDto>>> GetPublic(CancellationToken cancellationToken)
        {
            return Ok(await _settingsService.GetPublicAsync(cancellationToken));
        }

        [HttpGet]
        [HasPermission(PermissionModule.Settings, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<SettingDto>>> GetAll(CancellationToken cancellationToken)
        {
            return Ok(await _settingsService.GetAllAsync(cancellationToken));
        }

        /// <summary>Bulk upsert; returns the full settings list after saving.</summary>
        [HttpPut]
        [HasPermission(PermissionModule.Settings, PermissionAction.Edit)]
        public async Task<ActionResult<IReadOnlyList<SettingDto>>> Update(
            List<UpdateSettingRequest> updates,
            CancellationToken cancellationToken)
        {
            return Ok(await _settingsService.UpsertAsync(updates, cancellationToken));
        }
    }
}
