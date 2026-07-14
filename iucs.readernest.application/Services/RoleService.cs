using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Users;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class RoleService : IRoleService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;

        public RoleService(IUnitOfWork unitOfWork, IAuditLogService auditLog)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
        }

        public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken cancellationToken = default)
        {
            var roles = await _unitOfWork.Repository<RoleDefinition>().Query()
                .Include(r => r.Permissions)
                .OrderBy(r => r.DisplayName)
                .ToListAsync(cancellationToken);

            return roles.Select(ToDto).ToList();
        }

        public async Task<RoleDto> CreateAsync(SaveRoleRequest request, CancellationToken cancellationToken = default)
        {
            var name = NormalizeName(request.Name);
            ValidateDisplayName(request.DisplayName);

            var repository = _unitOfWork.Repository<RoleDefinition>();
            if (await repository.ExistsAsync(r => r.Name == name, cancellationToken))
            {
                throw new ConflictException($"A role named '{name}' already exists.");
            }

            var role = new RoleDefinition
            {
                Name = name,
                DisplayName = request.DisplayName.Trim(),
                Description = request.Description?.Trim(),
                DefaultRoute = NormalizeRoute(request.DefaultRoute),
                Permissions = MapPermissions(request.Permissions),
            };
            await repository.AddAsync(role, cancellationToken);

            await _auditLog.StageAsync(AuditAction.Create, nameof(RoleDefinition), name, cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(role);
        }

        public async Task<RoleDto> UpdateAsync(Guid id, SaveRoleRequest request, CancellationToken cancellationToken = default)
        {
            var name = NormalizeName(request.Name);
            ValidateDisplayName(request.DisplayName);

            var repository = _unitOfWork.Repository<RoleDefinition>();
            var role = await repository.Query()
                .Include(r => r.Permissions)
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(RoleDefinition), id);

            if (role.IsSystem && role.Name != name)
            {
                throw new DomainValidationException($"System role '{role.Name}' cannot be renamed.");
            }

            if (await repository.ExistsAsync(r => r.Id != id && r.Name == name, cancellationToken))
            {
                throw new ConflictException($"A role named '{name}' already exists.");
            }

            role.Name = name;
            role.DisplayName = request.DisplayName.Trim();
            role.Description = request.Description?.Trim();
            role.DefaultRoute = NormalizeRoute(request.DefaultRoute);

            // Replace-all semantics: the role editor submits the full matrix
            var permissionRepository = _unitOfWork.Repository<RolePermission>();
            foreach (var permission in role.Permissions.ToList())
            {
                permissionRepository.Remove(permission);
            }

            role.Permissions = MapPermissions(request.Permissions);
            repository.Update(role);

            await _auditLog.StageAsync(AuditAction.Update, nameof(RoleDefinition), name, cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(role);
        }

        public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var repository = _unitOfWork.Repository<RoleDefinition>();
            var role = await repository.Query()
                .Include(r => r.Permissions)
                .FirstOrDefaultAsync(r => r.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(RoleDefinition), id);

            if (role.IsSystem)
            {
                throw new DomainValidationException($"System role '{role.Name}' cannot be deleted.");
            }

            if (await _unitOfWork.Repository<User>().ExistsAsync(u => u.RoleDefinitionId == id, cancellationToken))
            {
                throw new ConflictException($"Role '{role.DisplayName}' is currently assigned to one or more users and cannot be deleted.");
            }

            var permissionRepository = _unitOfWork.Repository<RolePermission>();
            foreach (var permission in role.Permissions.ToList())
            {
                permissionRepository.Remove(permission);
            }

            repository.Remove(role);
            await _auditLog.StageAsync(AuditAction.Delete, nameof(RoleDefinition), role.Name, cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task<IReadOnlyList<PermissionDto>?> ResolvePermissionsAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            var key = name.Trim().ToLowerInvariant();
            var role = await _unitOfWork.Repository<RoleDefinition>().Query()
                .Include(r => r.Permissions)
                .FirstOrDefaultAsync(r => r.Name == key, cancellationToken);

            return role?.Permissions.Select(ToPermissionDto).ToList();
        }

        public async Task<RoleDto?> FindByNameAsync(string name, CancellationToken cancellationToken = default)
        {
            var key = name.Trim().ToLowerInvariant();
            var role = await _unitOfWork.Repository<RoleDefinition>().Query()
                .Include(r => r.Permissions)
                .FirstOrDefaultAsync(r => r.Name == key, cancellationToken);

            return role is null ? null : ToDto(role);
        }

        private static string NormalizeName(string name)
        {
            var key = name?.Trim().ToLowerInvariant() ?? string.Empty;
            if (key.Length == 0)
            {
                throw new DomainValidationException("Role name is required.");
            }

            return key;
        }

        private static void ValidateDisplayName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                throw new DomainValidationException("Role display name is required.");
            }
        }

        private static string? NormalizeRoute(string? route)
        {
            if (string.IsNullOrWhiteSpace(route))
            {
                return null;
            }

            var trimmed = route.Trim();
            if (!trimmed.StartsWith('/'))
            {
                throw new DomainValidationException("Default route must start with '/'.");
            }

            return trimmed;
        }

        private static List<RolePermission> MapPermissions(IReadOnlyList<PermissionDto> permissions)
        {
            var duplicate = permissions.GroupBy(p => p.Module).FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null)
            {
                throw new DomainValidationException($"Module '{duplicate.Key}' appears more than once.");
            }

            return permissions.Select(p => new RolePermission
            {
                Module = p.Module,
                CanView = p.CanView,
                CanCreate = p.CanCreate,
                CanEdit = p.CanEdit,
                CanDelete = p.CanDelete,
                CanApprove = p.CanApprove,
            }).ToList();
        }

        private static RoleDto ToDto(RoleDefinition role) => new()
        {
            Id = role.Id,
            Name = role.Name,
            DisplayName = role.DisplayName,
            Description = role.Description,
            DefaultRoute = role.DefaultRoute,
            IsSystem = role.IsSystem,
            Permissions = role.Permissions
                .OrderBy(p => p.Module)
                .Select(ToPermissionDto)
                .ToList(),
        };

        private static PermissionDto ToPermissionDto(RolePermission permission) => new()
        {
            Module = permission.Module,
            CanView = permission.CanView,
            CanCreate = permission.CanCreate,
            CanEdit = permission.CanEdit,
            CanDelete = permission.CanDelete,
            CanApprove = permission.CanApprove,
        };
    }
}
