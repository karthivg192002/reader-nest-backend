using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Navigation;
using iucs.readernest.domain.Entities.Navigation;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class MenuService : IMenuService
    {
        /// <summary>Portal keys matching the frontend role shells.</summary>
        public static readonly IReadOnlyList<string> Portals =
            ["admin", "teacher", "parent", "subadmin", "admission", "coordinator", "management", "student"];

        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;

        public MenuService(IUnitOfWork unitOfWork, IAuditLogService auditLog)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
        }

        public async Task<IReadOnlyList<MenuItemDto>> GetForUserAsync(
            Guid userId,
            UserRole role,
            IReadOnlyCollection<PermissionModule> viewableModules,
            CancellationToken cancellationToken = default)
        {
            var isAdmin = role == UserRole.Admin;
            var (key, roleDefinitionId) = await ResolvePortalAndRoleAsync(userId, role, cancellationToken);

            // Every active item, not just the caller's home portal — an explicit grant on a
            // foreign-portal item (Menu Access) can make it reachable cross-portal (see
            // RequireAuth's matching accessiblePortals check on the frontend). The legacy
            // fallback below stays scoped to the home portal only, so this alone changes
            // nothing for anyone who's never touched Menu Access.
            var items = await _unitOfWork.Repository<MenuItem>().Query()
                .Where(m => m.IsActive)
                .OrderBy(m => m.Portal).ThenBy(m => m.SectionOrder).ThenBy(m => m.SortOrder)
                .ToListAsync(cancellationToken);

            var grants = new Dictionary<Guid, MenuPermission>();
            if (roleDefinitionId is { } id)
            {
                var menuIds = items.Select(m => m.Id).ToList();
                grants = await _unitOfWork.Repository<MenuPermission>().Query()
                    .Where(p => p.RoleDefinitionId == id && menuIds.Contains(p.MenuItemId))
                    .ToDictionaryAsync(p => p.MenuItemId, cancellationToken);
            }

            // Phase 3 of the menu/role redesign: once a role has been explicitly configured
            // for a menu item via Menu Access, that grant is authoritative (whatever it says,
            // including "hidden"), regardless of which portal the item belongs to. A menu item
            // nobody has touched in the new grid yet — no row at all — keeps behaving exactly
            // as before: visible only within the caller's own home portal, when unrequired or
            // gated by the caller's module-level View grant (Admin bypasses); a foreign-portal
            // item with no explicit grant stays invisible, so nothing regresses for menus the
            // new page hasn't been used on.
            var visible = items.Where(m =>
                grants.TryGetValue(m.Id, out var grant)
                    ? grant.CanView
                    : m.Portal == key && (m.RequiredModule is null || isAdmin || viewableModules.Contains(m.RequiredModule.Value)));

            // Create/Edit/Delete/Approve for the frontend to gate its own buttons on (this app
            // never did that before today — every admin page showed Add/Edit/Delete
            // unconditionally). Same "explicit grant wins, otherwise nothing changes" rule as
            // View's own fallback above, except the *unconfigured* default is permissive (true)
            // rather than restrictive: unlike View, these actions have never been gated by
            // anything before, so defaulting an untouched item to false would silently hide
            // buttons across the whole app the moment this shipped. An explicit Menu Access
            // grant (even one that's all-false) is what actually restricts them.
            return visible.Select(m =>
            {
                grants.TryGetValue(m.Id, out var grant);
                var dto = ToDto(m);
                dto.CanCreate = grant?.CanCreate ?? true;
                dto.CanEdit = grant?.CanEdit ?? true;
                dto.CanDelete = grant?.CanDelete ?? true;
                dto.CanApprove = grant?.CanApprove ?? true;
                return dto;
            }).ToList();
        }

        public async Task<IReadOnlyList<string>> GetModulePermissionClaimsAsync(
            Guid userId, UserRole role, CancellationToken cancellationToken = default)
        {
            var (_, roleDefinitionId) = await ResolvePortalAndRoleAsync(userId, role, cancellationToken);
            if (roleDefinitionId is not { } id)
            {
                return [];
            }

            var grants = await _unitOfWork.Repository<MenuPermission>().Query()
                .Include(p => p.MenuItem)
                .Where(p => p.RoleDefinitionId == id)
                .ToListAsync(cancellationToken);

            // Module-aggregated enforcement: every [HasPermission] check still reads a
            // "Module:Action" claim, so a menu's grant is rolled up (OR'd) into whichever
            // PermissionModule its MenuItem.RequiredModule names — a menu with no
            // RequiredModule has nothing to aggregate into and is skipped here (it's a pure
            // visibility gate; GetForUserAsync above already handles that). Two menu items
            // sharing one module, only one granted CanEdit, both end up Edit-enabled at the
            // API — a known, accepted limitation of keeping every existing [HasPermission]
            // attribute unchanged rather than rewriting all of them to reference a specific
            // menu item; the Roles & Menu Access page carries a note saying so.
            var byModule = new Dictionary<PermissionModule, (bool View, bool Create, bool Edit, bool Delete, bool Approve)>();
            foreach (var grant in grants)
            {
                if (grant.MenuItem.RequiredModule is not { } module)
                {
                    continue;
                }

                var current = byModule.TryGetValue(module, out var existing) ? existing : default;
                byModule[module] = (
                    current.View || grant.CanView,
                    current.Create || grant.CanCreate,
                    current.Edit || grant.CanEdit,
                    current.Delete || grant.CanDelete,
                    current.Approve || grant.CanApprove);
            }

            return ToClaims(byModule);
        }

        private static List<string> ToClaims(
            IReadOnlyDictionary<PermissionModule, (bool View, bool Create, bool Edit, bool Delete, bool Approve)> byModule)
        {
            var claims = new List<string>();
            foreach (var (module, grant) in byModule)
            {
                if (grant.View) claims.Add($"{module}:{PermissionAction.View}");
                if (grant.Create) claims.Add($"{module}:{PermissionAction.Create}");
                if (grant.Edit) claims.Add($"{module}:{PermissionAction.Edit}");
                if (grant.Delete) claims.Add($"{module}:{PermissionAction.Delete}");
                if (grant.Approve) claims.Add($"{module}:{PermissionAction.Approve}");
            }

            return claims;
        }

        /// <summary>
        /// Portal key and governing RoleDefinition for a user, resolved together since Sub
        /// Admin needs the same lookup for both. Sub Admins take the portal from their assigned
        /// preset's DefaultRoute (e.g. "/coordinator/..." → "coordinator") so a
        /// Coordinator/Management preset lands on its own sidebar and its menu grants come from
        /// that shared preset; a Sub Admin with no preset explicitly applied (RoleDefinitionId is
        /// only ever set by account creation with a preset, or "Apply preset…" — plain per-user
        /// permission edits and access-request approval never touch it, so this is the common
        /// case, not an edge case) falls back to the base "sub-admin" system RoleDefinition
        /// ("Parent Relationship Manager") by name — mirroring exactly how Teacher/Parent/
        /// AdmissionTeam below resolve to their own named system role. Confirmed live: a plain
        /// Relationship Manager account (no preset ever applied) kept the pre-Phase-3 menu
        /// visibility no matter what was configured for them in Menu Access, because this used
        /// to return a bare null RoleDefinitionId instead of that base preset.
        /// </summary>
        private async Task<(string Portal, Guid? RoleDefinitionId)> ResolvePortalAndRoleAsync(
            Guid userId, UserRole role, CancellationToken cancellationToken)
        {
            if (role == UserRole.SubAdmin)
            {
                var user = await _unitOfWork.Repository<domain.Entities.Users.User>()
                    .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
                if (user?.RoleDefinitionId is { } roleId)
                {
                    var roleDef = await _unitOfWork.Repository<domain.Entities.Users.RoleDefinition>()
                        .GetByIdAsync(roleId, cancellationToken);
                    if (roleDef is not null)
                    {
                        var segment = roleDef.DefaultRoute?.Trim('/').Split('/').FirstOrDefault();
                        var portal = !string.IsNullOrWhiteSpace(segment) && Portals.Contains(segment) ? segment : "subadmin";
                        return (portal, roleDef.Id);
                    }
                }

                var baseRole = await _unitOfWork.Repository<domain.Entities.Users.RoleDefinition>()
                    .FirstOrDefaultAsync(r => r.Name == "sub-admin", cancellationToken);
                return ("subadmin", baseRole?.Id);
            }

            var portalKey = role switch
            {
                UserRole.Admin => "admin",
                UserRole.Teacher => "teacher",
                UserRole.Parent => "parent",
                UserRole.AdmissionTeam => "admission",
                _ => "admin",
            };

            var systemRoleName = role switch
            {
                UserRole.Admin => "admin",
                UserRole.Teacher => "teacher",
                UserRole.Parent => "parent",
                UserRole.AdmissionTeam => "admission",
                _ => (string?)null,
            };
            if (systemRoleName is null)
            {
                return (portalKey, null);
            }

            var systemRole = await _unitOfWork.Repository<domain.Entities.Users.RoleDefinition>()
                .FirstOrDefaultAsync(r => r.Name == systemRoleName, cancellationToken);
            return (portalKey, systemRole?.Id);
        }

        public async Task<IReadOnlyList<MenuItemDto>> ListAsync(
            string? portal,
            CancellationToken cancellationToken = default)
        {
            var query = _unitOfWork.Repository<MenuItem>().Query();
            if (!string.IsNullOrWhiteSpace(portal))
            {
                var key = NormalizePortal(portal);
                query = query.Where(m => m.Portal == key);
            }

            var items = await query
                .OrderBy(m => m.Portal).ThenBy(m => m.SectionOrder).ThenBy(m => m.SortOrder)
                .ToListAsync(cancellationToken);

            return items.Select(ToDto).ToList();
        }

        public async Task<MenuItemDto> CreateAsync(
            SaveMenuItemRequest request,
            CancellationToken cancellationToken = default)
        {
            Validate(request);
            var repository = _unitOfWork.Repository<MenuItem>();
            var portal = NormalizePortal(request.Portal);
            var path = request.Path.Trim();

            if (await repository.ExistsAsync(m => m.Portal == portal && m.Path == path, cancellationToken))
            {
                throw new ConflictException($"The {portal} portal already has a menu item for '{path}'.");
            }

            var item = new MenuItem();
            Apply(item, request, portal, path);
            await repository.AddAsync(item, cancellationToken);

            await _auditLog.StageAsync(AuditAction.Create, nameof(MenuItem), path, cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(item);
        }

        public async Task<MenuItemDto> UpdateAsync(
            Guid id,
            SaveMenuItemRequest request,
            CancellationToken cancellationToken = default)
        {
            Validate(request);
            var repository = _unitOfWork.Repository<MenuItem>();
            var item = await repository.GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(MenuItem), id);

            var portal = NormalizePortal(request.Portal);
            var path = request.Path.Trim();
            if (await repository.ExistsAsync(
                    m => m.Id != id && m.Portal == portal && m.Path == path, cancellationToken))
            {
                throw new ConflictException($"The {portal} portal already has a menu item for '{path}'.");
            }

            Apply(item, request, portal, path);
            repository.Update(item);

            await _auditLog.StageAsync(AuditAction.Update, nameof(MenuItem), id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(item);
        }

        public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var repository = _unitOfWork.Repository<MenuItem>();
            var item = await repository.GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(MenuItem), id);

            repository.Remove(item);
            await _auditLog.StageAsync(AuditAction.Delete, nameof(MenuItem), id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        private static string NormalizePortal(string portal)
        {
            var key = portal.Trim().ToLowerInvariant();
            if (!Portals.Contains(key))
            {
                throw new DomainValidationException(
                    $"Unknown portal '{portal}'. Available: {string.Join(", ", Portals)}.");
            }

            return key;
        }

        private static void Validate(SaveMenuItemRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Label))
            {
                throw new DomainValidationException("Menu label is required.");
            }

            if (string.IsNullOrWhiteSpace(request.Path) || !request.Path.Trim().StartsWith('/'))
            {
                throw new DomainValidationException("Menu path is required and must start with '/'.");
            }

            if (string.IsNullOrWhiteSpace(request.Icon))
            {
                throw new DomainValidationException("Menu icon is required.");
            }
        }

        private static void Apply(MenuItem item, SaveMenuItemRequest request, string portal, string path)
        {
            item.Portal = portal;
            item.Section = string.IsNullOrWhiteSpace(request.Section) ? null : request.Section.Trim();
            item.SectionOrder = request.SectionOrder;
            item.Label = request.Label.Trim();
            item.Path = path;
            item.Icon = request.Icon.Trim();
            item.SortOrder = request.SortOrder;
            item.IsActive = request.IsActive;
            item.RequiredModule = request.RequiredModule;
        }

        private static MenuItemDto ToDto(MenuItem item) => new()
        {
            Id = item.Id,
            Portal = item.Portal,
            Section = item.Section,
            SectionOrder = item.SectionOrder,
            Label = item.Label,
            Path = item.Path,
            Icon = item.Icon,
            SortOrder = item.SortOrder,
            IsActive = item.IsActive,
            RequiredModule = item.RequiredModule,
        };
    }
}
