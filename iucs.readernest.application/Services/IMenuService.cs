using iucs.readernest.application.Dto.Navigation;

namespace iucs.readernest.application.Services
{
    public interface IMenuService
    {
        /// <summary>Active items of one portal in sidebar order, for rendering navigation.</summary>
        Task<IReadOnlyList<MenuItemDto>> GetForPortalAsync(string portal, CancellationToken cancellationToken = default);

        /// <summary>All items (including inactive), optionally filtered by portal, for the admin menu manager.</summary>
        Task<IReadOnlyList<MenuItemDto>> ListAsync(string? portal, CancellationToken cancellationToken = default);

        Task<MenuItemDto> CreateAsync(SaveMenuItemRequest request, CancellationToken cancellationToken = default);

        Task<MenuItemDto> UpdateAsync(Guid id, SaveMenuItemRequest request, CancellationToken cancellationToken = default);

        Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    }
}
