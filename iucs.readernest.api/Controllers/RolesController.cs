using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Users;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// DB-maintained permission roles (presets). Applying a role to a Sub Admin
    /// goes through PUT /api/users/{id}/permissions/preset/{name}.
    /// </summary>
    [ApiController]
    [Route("api/roles")]
    public class RolesController : ControllerBase
    {
        private readonly IRoleService _roleService;

        public RolesController(IRoleService roleService)
        {
            _roleService = roleService;
        }

        [HttpGet]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.View)]
        public async Task<ActionResult<IReadOnlyList<RoleDto>>> List(CancellationToken cancellationToken)
        {
            return Ok(await _roleService.ListAsync(cancellationToken));
        }

        [HttpPost]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Create)]
        public async Task<ActionResult<RoleDto>> Create(SaveRoleRequest request, CancellationToken cancellationToken)
        {
            var role = await _roleService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(List), null, role);
        }

        [HttpPut("{id:guid}")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Edit)]
        public async Task<ActionResult<RoleDto>> Update(Guid id, SaveRoleRequest request, CancellationToken cancellationToken)
        {
            return Ok(await _roleService.UpdateAsync(id, request, cancellationToken));
        }

        [HttpDelete("{id:guid}")]
        [HasPermission(PermissionModule.UserManagement, PermissionAction.Delete)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            await _roleService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
    }
}
