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
            var key = await ResolvePortalAsync(userId, role, cancellationToken);

            var items = await _unitOfWork.Repository<MenuItem>().Query()
                .Where(m => m.Portal == key && m.IsActive)
                .OrderBy(m => m.SectionOrder).ThenBy(m => m.SortOrder)
                .ToListAsync(cancellationToken);

            // A menu item with no RequiredModule is always visible; a gated item shows
            // only when the user's assigned role grants View on that module (Admin bypasses).
            var visible = items.Where(m =>
                m.RequiredModule is null
                || isAdmin
                || viewableModules.Contains(m.RequiredModule.Value));

            return visible.Select(ToDto).ToList();
        }

        /// <summary>
        /// Portal key for a user. Sub Admins take the portal from their assigned role's
        /// DefaultRoute (e.g. "/coordinator/..." → "coordinator") so a Coordinator/Management
        /// preset lands on its own sidebar; everyone else maps straight from the account role.
        /// </summary>
        private async Task<string> ResolvePortalAsync(Guid userId, UserRole role, CancellationToken cancellationToken)
        {
            if (role == UserRole.SubAdmin)
            {
                var user = await _unitOfWork.Repository<domain.Entities.Users.User>()
                    .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
                if (user?.RoleDefinitionId is { } roleId)
                {
                    var roleDef = await _unitOfWork.Repository<domain.Entities.Users.RoleDefinition>()
                        .GetByIdAsync(roleId, cancellationToken);
                    var segment = roleDef?.DefaultRoute?.Trim('/').Split('/').FirstOrDefault();
                    if (!string.IsNullOrWhiteSpace(segment) && Portals.Contains(segment))
                    {
                        return segment;
                    }
                }

                return "subadmin";
            }

            return role switch
            {
                UserRole.Admin => "admin",
                UserRole.Teacher => "teacher",
                UserRole.Parent => "parent",
                UserRole.AdmissionTeam => "admission",
                _ => "admin",
            };
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
