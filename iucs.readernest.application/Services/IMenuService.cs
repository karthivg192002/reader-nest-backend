using iucs.readernest.application.Dto.Navigation;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Services
{
    public interface IMenuService
    {
        /// <summary>
        /// The signed-in user's sidebar: resolves the portal from the user's account role
        /// (Sub Admins use their assigned role's default-route portal), then for each item
        /// prefers an explicit menu_permissions grant for the caller's RoleDefinition when one
        /// exists (Menu Access), falling back to the legacy RequiredModule/View-grant gate for
        /// any item nobody has configured in the new grid yet. Admins bypass the legacy gate
        /// (not an explicit grant) so an unconfigured item never disappears from their own view.
        /// </summary>
        Task<IReadOnlyList<MenuItemDto>> GetForUserAsync(
            Guid userId,
            UserRole role,
            IReadOnlyCollection<PermissionModule> viewableModules,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// The real API-authorization claims for a user, aggregated from Menu Access instead of
        /// the legacy RolePermission/SubAdminPermission tables: resolves the same governing
        /// RoleDefinition GetForUserAsync does, then for every menu item that RoleDefinition has
        /// been granted anything on, ORs each of View/Create/Edit/Delete/Approve into whichever
        /// PermissionModule the item's RequiredModule names (a menu with no RequiredModule
        /// contributes nothing here — it's a pure visibility gate). Returns "Module:Action"
        /// strings in the same shape the JWT "perm" claims already use, so every existing
        /// [HasPermission] check keeps working unchanged. Empty for Admin (implicit bypass) and
        /// for any role with no resolvable RoleDefinition.
        /// </summary>
        Task<IReadOnlyList<string>> GetModulePermissionClaimsAsync(
            Guid userId, UserRole role, CancellationToken cancellationToken = default);

        /// <summary>All items (including inactive), optionally filtered by portal, for the admin menu manager.</summary>
        Task<IReadOnlyList<MenuItemDto>> ListAsync(string? portal, CancellationToken cancellationToken = default);

        Task<MenuItemDto> CreateAsync(SaveMenuItemRequest request, CancellationToken cancellationToken = default);

        Task<MenuItemDto> UpdateAsync(Guid id, SaveMenuItemRequest request, CancellationToken cancellationToken = default);

        Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    }
}
