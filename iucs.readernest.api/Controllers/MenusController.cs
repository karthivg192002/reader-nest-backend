using System.Security.Claims;
using iucs.readernest.api.Auth;
using iucs.readernest.application.Dto.Navigation;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    [ApiController]
    [Route("api/menus")]
    public class MenusController : ControllerBase
    {
        private readonly IMenuService _menuService;

        public MenusController(IMenuService menuService)
        {
            _menuService = menuService;
        }

        /// <summary>
        /// The signed-in user's own sidebar, resolved from their account role's portal and
        /// filtered by the module grants their assigned role carries (same "perm" claims as
        /// [HasPermission]). This is what the app shell loads so the menu reflects the role
        /// assigned to the user, not a hard-coded per-portal list.
        /// </summary>
        [HttpGet("mine")]
        [Authorize]
        public async Task<ActionResult<IReadOnlyList<MenuItemDto>>> GetMyMenu(CancellationToken cancellationToken)
        {
            var userId = Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var role = Enum.TryParse<UserRole>(User.FindFirstValue(ClaimTypes.Role), out var r) ? r : UserRole.Admin;

            // "perm" claims are "Module:Action" — the module half is a PermissionModuleDefinition
            // key (built-in enum name or a custom module), not necessarily a parseable enum value.
            var viewable = User.FindAll(JwtTokenService.PermissionClaimType)
                .Select(c => c.Value)
                .Where(v => v.EndsWith($":{PermissionAction.View}", StringComparison.Ordinal))
                .Select(v => v.Split(':')[0])
                .Distinct()
                .ToList();

            return Ok(await _menuService.GetForUserAsync(userId, role, viewable, cancellationToken));
        }

        /// <summary>
        /// Every configured item including inactive ones. Used by the admin menu manager, but
        /// also by every "My Permissions" screen (Admin's Roles &amp; Permissions, and a Sub
        /// Admin's own read-only view) purely to caption a module with which real menus it
        /// gates — [Authorize]-only rather than Settings-gated, since a menu item's label/path
        /// isn't sensitive and most Sub Admins were never granted Settings themselves
        /// (confirmed live: a Sub Admin's own "My Permissions" page 403'd loading this).
        /// </summary>
        [HttpGet]
        [Authorize]
        public async Task<ActionResult<IReadOnlyList<MenuItemDto>>> List(
            [FromQuery] string? portal,
            CancellationToken cancellationToken)
        {
            return Ok(await _menuService.ListAsync(portal, cancellationToken));
        }

        [HttpPost]
        [HasPermission(PermissionModule.Settings, PermissionAction.Edit)]
        public async Task<ActionResult<MenuItemDto>> Create(SaveMenuItemRequest request, CancellationToken cancellationToken)
        {
            var item = await _menuService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(List), new { portal = item.Portal }, item);
        }

        [HttpPut("{id:guid}")]
        [HasPermission(PermissionModule.Settings, PermissionAction.Edit)]
        public async Task<ActionResult<MenuItemDto>> Update(
            Guid id,
            SaveMenuItemRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _menuService.UpdateAsync(id, request, cancellationToken));
        }

        [HttpDelete("{id:guid}")]
        [HasPermission(PermissionModule.Settings, PermissionAction.Edit)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            await _menuService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
    }
}
