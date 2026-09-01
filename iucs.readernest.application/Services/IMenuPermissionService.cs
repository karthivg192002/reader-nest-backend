using iucs.readernest.application.Dto.Navigation;

namespace iucs.readernest.application.Services
{
    public interface IMenuPermissionService
    {
        /// <summary>
        /// Every active menu item with this role's current View/Create/Edit/Delete grant
        /// (all-false when no MenuPermission row exists yet for that item).
        /// </summary>
        Task<IReadOnlyList<MenuPermissionDto>> GetForRoleAsync(Guid roleDefinitionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Replace-all save of a role's menu grants, mirroring RoleService.UpdateAsync: every
        /// existing grant for this role is removed, then one row is re-added per submitted item,
        /// all-false included — an explicit "no access" row is what tells MenuService this menu
        /// item has been configured for this role at all, as opposed to never touched.
        /// </summary>
        Task<IReadOnlyList<MenuPermissionDto>> SetForRoleAsync(
            Guid roleDefinitionId,
            IReadOnlyList<SaveMenuPermissionItem> items,
            CancellationToken cancellationToken = default);
    }
}
