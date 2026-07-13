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

        /// <summary>Active sidebar items of one portal; any signed-in user can render their own navigation.</summary>
        [HttpGet("portal/{portal}")]
        [Authorize]
        public async Task<ActionResult<IReadOnlyList<MenuItemDto>>> GetPortalMenu(
            string portal,
            CancellationToken cancellationToken)
        {
            return Ok(await _menuService.GetForPortalAsync(portal, cancellationToken));
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
