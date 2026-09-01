using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Users;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// The grantable permission-module catalog (built-in + Admin-defined custom ones). Same
    /// Settings gate as RolesController/MenusController: this is platform configuration a
    /// role's permission matrix is built from, not routine user-record management.
    /// </summary>
    [ApiController]
    [Route("api/permission-modules")]
    public class PermissionModulesController : ControllerBase
    {
        private readonly IPermissionModuleService _permissionModuleService;

        public PermissionModulesController(IPermissionModuleService permissionModuleService)
        {
            _permissionModuleService = permissionModuleService;
        }

        [HttpGet]
        [HasPermission(PermissionModule.Settings, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<PermissionModuleDefinitionDto>>> List(CancellationToken cancellationToken)
        {
            return Ok(await _permissionModuleService.ListAsync(cancellationToken));
        }

        [HttpPost]
        [HasPermission(PermissionModule.Settings, PermissionAction.Edit)]
        public async Task<ActionResult<PermissionModuleDefinitionDto>> Create(
            SavePermissionModuleDefinitionRequest request, CancellationToken cancellationToken)
        {
            var module = await _permissionModuleService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(List), null, module);
        }

        [HttpDelete("{id:guid}")]
        [HasPermission(PermissionModule.Settings, PermissionAction.Edit)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            await _permissionModuleService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
    }
}
