using iucs.readernest.application.Dto.Navigation;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Services
{
    public interface IMenuService
    {
        /// <summary>
        /// The signed-in user's sidebar: resolves the portal from the user's account role
        /// (Sub Admins use their assigned role's default-route portal), then drops any item
        /// whose RequiredModule the user's role does not grant View on. Admins see all.
        /// </summary>
        Task<IReadOnlyList<MenuItemDto>> GetForUserAsync(
            Guid userId,
            UserRole role,
            IReadOnlyCollection<PermissionModule> viewableModules,
            CancellationToken cancellationToken = default);

        /// <summary>All items (including inactive), optionally filtered by portal, for the admin menu manager.</summary>
        Task<IReadOnlyList<MenuItemDto>> ListAsync(string? portal, CancellationToken cancellationToken = default);

        Task<MenuItemDto> CreateAsync(SaveMenuItemRequest request, CancellationToken cancellationToken = default);

        Task<MenuItemDto> UpdateAsync(Guid id, SaveMenuItemRequest request, CancellationToken cancellationToken = default);

        Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    }
}
