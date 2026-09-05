using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Users;
using iucs.readernest.domain.Entities.Navigation;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class PermissionModuleService : IPermissionModuleService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;

        public PermissionModuleService(IUnitOfWork unitOfWork, IAuditLogService auditLog)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
        }

        public async Task<IReadOnlyList<PermissionModuleDefinitionDto>> ListAsync(CancellationToken cancellationToken = default)
        {
            var modules = await _unitOfWork.Repository<PermissionModuleDefinition>().Query()
                .OrderBy(m => m.SortOrder).ThenBy(m => m.Label)
                .ToListAsync(cancellationToken);

            return modules.Select(ToDto).ToList();
        }

        public async Task<PermissionModuleDefinitionDto> CreateAsync(
            SavePermissionModuleDefinitionRequest request,
            CancellationToken cancellationToken = default)
        {
            var key = request.Key.Trim();
            if (key.Length == 0 || key.Any(c => !char.IsLetterOrDigit(c)))
            {
                throw new DomainValidationException("Module key must contain only letters and digits (no spaces, no punctuation).");
            }

            if (string.IsNullOrWhiteSpace(request.Label))
            {
                throw new DomainValidationException("Module label is required.");
            }

            var repository = _unitOfWork.Repository<PermissionModuleDefinition>();
            if (await repository.ExistsAsync(m => m.Key.ToLower() == key.ToLower(), cancellationToken))
            {
                throw new ConflictException($"A permission module with key '{key}' already exists.");
            }

            var maxSortOrder = await repository.Query().Select(m => (int?)m.SortOrder).MaxAsync(cancellationToken) ?? -1;
            var module = new PermissionModuleDefinition
            {
                Key = key,
                Label = request.Label.Trim(),
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                IsSystem = false,
                SortOrder = maxSortOrder + 1,
            };
            await repository.AddAsync(module, cancellationToken);

            await _auditLog.StageAsync(AuditAction.Create, nameof(PermissionModuleDefinition), key, cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(module);
        }

        public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var repository = _unitOfWork.Repository<PermissionModuleDefinition>();
            var module = await repository.GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(PermissionModuleDefinition), id);

            if (module.IsSystem)
            {
                throw new DomainValidationException($"'{module.Label}' is a built-in module and can't be deleted.");
            }

            var inUseByRole = await _unitOfWork.Repository<RolePermission>().ExistsAsync(p => p.Module == module.Key, cancellationToken);
            var inUseBySubAdmin = await _unitOfWork.Repository<SubAdminPermission>().ExistsAsync(p => p.Module == module.Key, cancellationToken);
            var inUseByMenu = await _unitOfWork.Repository<MenuItem>().ExistsAsync(m => m.RequiredModule == module.Key, cancellationToken);
            if (inUseByRole || inUseBySubAdmin || inUseByMenu)
            {
                throw new ConflictException($"'{module.Label}' is still referenced by a role, a Sub Admin's grants, or a menu item, and can't be deleted.");
            }

            repository.Remove(module);
            await _auditLog.StageAsync(AuditAction.Delete, nameof(PermissionModuleDefinition), module.Key, cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        private static PermissionModuleDefinitionDto ToDto(PermissionModuleDefinition module) => new()
        {
            Id = module.Id,
            Key = module.Key,
            Label = module.Label,
            Description = module.Description,
            IsSystem = module.IsSystem,
            SortOrder = module.SortOrder,
        };
    }
}
