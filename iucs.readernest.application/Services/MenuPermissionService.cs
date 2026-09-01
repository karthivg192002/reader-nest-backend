using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Navigation;
using iucs.readernest.domain.Entities.Navigation;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class MenuPermissionService : IMenuPermissionService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;

        public MenuPermissionService(IUnitOfWork unitOfWork, IAuditLogService auditLog)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
        }

        public async Task<IReadOnlyList<MenuPermissionDto>> GetForRoleAsync(
            Guid roleDefinitionId,
            CancellationToken cancellationToken = default)
        {
            await EnsureRoleExistsAsync(roleDefinitionId, cancellationToken);

            var menuItems = await ActiveMenuItemsAsync(cancellationToken);
            var grants = await _unitOfWork.Repository<MenuPermission>().Query()
                .Where(p => p.RoleDefinitionId == roleDefinitionId)
                .ToDictionaryAsync(p => p.MenuItemId, cancellationToken);

            return ToDtos(menuItems, grants);
        }

        public async Task<IReadOnlyList<MenuPermissionDto>> SetForRoleAsync(
            Guid roleDefinitionId,
            IReadOnlyList<SaveMenuPermissionItem> items,
            CancellationToken cancellationToken = default)
        {
            await EnsureRoleExistsAsync(roleDefinitionId, cancellationToken);

            var menuItems = await ActiveMenuItemsAsync(cancellationToken);
            var validMenuItemIds = menuItems.Select(m => m.Id).ToHashSet();
            var unknown = items.Select(i => i.MenuItemId).Where(id => !validMenuItemIds.Contains(id)).ToList();
            if (unknown.Count > 0)
            {
                throw new DomainValidationException(
                    $"Unknown or inactive menu item(s): {string.Join(", ", unknown)}.");
            }

            var duplicate = items.GroupBy(i => i.MenuItemId).FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null)
            {
                throw new DomainValidationException($"Menu item '{duplicate.Key}' appears more than once.");
            }

            // Replace-all semantics: the grid submits the whole matrix, including deliberately
            // unchecked boxes, so every existing grant for this role is removed first.
            var permissionRepository = _unitOfWork.Repository<MenuPermission>();
            var existing = await permissionRepository.TrackedQuery()
                .Where(p => p.RoleDefinitionId == roleDefinitionId)
                .ToListAsync(cancellationToken);
            foreach (var grant in existing)
            {
                permissionRepository.Remove(grant);
            }

            var saved = new Dictionary<Guid, MenuPermission>();
            foreach (var item in items)
            {
                if (!item.CanView && !item.CanCreate && !item.CanEdit && !item.CanDelete)
                {
                    continue; // all-false rows are dropped rather than stored ("no row = no access")
                }

                var grant = new MenuPermission
                {
                    MenuItemId = item.MenuItemId,
                    RoleDefinitionId = roleDefinitionId,
                    CanView = item.CanView,
                    CanCreate = item.CanCreate,
                    CanEdit = item.CanEdit,
                    CanDelete = item.CanDelete,
                };
                await permissionRepository.AddAsync(grant, cancellationToken);
                saved[item.MenuItemId] = grant;
            }

            await _auditLog.StageAsync(AuditAction.Update, nameof(MenuPermission), roleDefinitionId.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return ToDtos(menuItems, saved);
        }

        private async Task EnsureRoleExistsAsync(Guid roleDefinitionId, CancellationToken cancellationToken)
        {
            if (!await _unitOfWork.Repository<RoleDefinition>().ExistsAsync(r => r.Id == roleDefinitionId, cancellationToken))
            {
                throw new NotFoundException(nameof(RoleDefinition), roleDefinitionId);
            }
        }

        private async Task<List<MenuItem>> ActiveMenuItemsAsync(CancellationToken cancellationToken) =>
            await _unitOfWork.Repository<MenuItem>().Query()
                .Where(m => m.IsActive)
                .OrderBy(m => m.Portal).ThenBy(m => m.SectionOrder).ThenBy(m => m.SortOrder)
                .ToListAsync(cancellationToken);

        private static List<MenuPermissionDto> ToDtos(
            IReadOnlyList<MenuItem> menuItems,
            IReadOnlyDictionary<Guid, MenuPermission> grants) =>
            menuItems.Select(m =>
            {
                grants.TryGetValue(m.Id, out var grant);
                return new MenuPermissionDto
                {
                    MenuItemId = m.Id,
                    MenuLabel = m.Label,
                    MenuPath = m.Path,
                    CanView = grant?.CanView ?? false,
                    CanCreate = grant?.CanCreate ?? false,
                    CanEdit = grant?.CanEdit ?? false,
                    CanDelete = grant?.CanDelete ?? false,
                };
            }).ToList();
    }
}
