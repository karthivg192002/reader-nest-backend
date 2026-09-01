using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Navigation;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// Phase 1 of the menu/role redesign: direct menu-item-to-role grants, additive alongside
    /// the existing module-level RolePermission table. Gated on Settings, same as
    /// RolesController and MenusController — this IS platform configuration.
    /// </summary>
    [ApiController]
    [Route("api/roles/{roleId:guid}/menu-permissions")]
    public class MenuPermissionsController : ControllerBase
    {
        private readonly IMenuPermissionService _menuPermissionService;

        public MenuPermissionsController(IMenuPermissionService menuPermissionService)
        {
            _menuPermissionService = menuPermissionService;
        }

        [HttpGet]
        [HasPermission(PermissionModule.Settings, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<MenuPermissionDto>>> Get(Guid roleId, CancellationToken cancellationToken)
        {
            return Ok(await _menuPermissionService.GetForRoleAsync(roleId, cancellationToken));
        }

        [HttpPut]
        [HasPermission(PermissionModule.Settings, PermissionAction.Edit)]
        public async Task<ActionResult<IReadOnlyList<MenuPermissionDto>>> Set(
            Guid roleId,
            List<SaveMenuPermissionItem> items,
            CancellationToken cancellationToken)
        {
            return Ok(await _menuPermissionService.SetForRoleAsync(roleId, items, cancellationToken));
        }
    }
}
