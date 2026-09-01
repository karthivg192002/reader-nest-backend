using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Users;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// The grantable permission-module catalog (built-in + Admin-defined custom ones).
    /// Creating/deleting a module is Settings-gated (platform configuration), same as
    /// RolesController/MenusController's write actions — but reading the list is just
    /// [Authorize], not Settings-gated: every signed-in Sub Admin needs it too, to render
    /// their own read-only "My Permissions" view and dashboard, and most Sub Admins were
    /// never granted Settings themselves (confirmed live: a Sub Admin with only Billing &
    /// Finance got a 403 loading their own dashboard). Mirrors MenusController.GetMyMenu's
    /// same [Authorize]-only pattern for the same reason.
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
        [Authorize]
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
