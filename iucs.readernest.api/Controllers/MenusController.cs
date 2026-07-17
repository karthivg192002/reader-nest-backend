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

            // "perm" claims are "Module:Action"; collect the modules the role can View.
            var viewable = User.FindAll(JwtTokenService.PermissionClaimType)
                .Select(c => c.Value)
                .Where(v => v.EndsWith($":{PermissionAction.View}", StringComparison.Ordinal))
                .Select(v => Enum.TryParse<PermissionModule>(v.Split(':')[0], out var m) ? (PermissionModule?)m : null)
                .Where(m => m.HasValue)
                .Select(m => m!.Value)
                .Distinct()
                .ToList();

            return Ok(await _menuService.GetForUserAsync(userId, role, viewable, cancellationToken));
        }

        /// <summary>Every configured item including inactive ones, for the admin menu manager.</summary>
        [HttpGet]
        [HasPermission(PermissionModule.Settings, PermissionAction.View)]
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
