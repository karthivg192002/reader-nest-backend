using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Settings;
using iucs.readernest.domain.Entities.Settings;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class SettingsService : ISettingsService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;

        public SettingsService(IUnitOfWork unitOfWork, IAuditLogService auditLog)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
        }

        public async Task<IReadOnlyList<SettingDto>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var settings = await _unitOfWork.Repository<AppSetting>().Query()
                .OrderBy(s => s.Category).ThenBy(s => s.Key)
                .ToListAsync(cancellationToken);

            return settings.Select(ToDto).ToList();
        }

        public async Task<IReadOnlyList<SettingDto>> GetPublicAsync(CancellationToken cancellationToken = default)
        {
            var settings = await _unitOfWork.Repository<AppSetting>().Query()
                .Where(s => s.IsPublic)
                .OrderBy(s => s.Key)
                .ToListAsync(cancellationToken);

            return settings.Select(ToDto).ToList();
        }

        public async Task<IReadOnlyList<SettingDto>> UpsertAsync(
            IReadOnlyList<UpdateSettingRequest> updates,
            CancellationToken cancellationToken = default)
        {
            if (updates.Count == 0)
            {
                throw new DomainValidationException("At least one setting must be provided.");
            }

            var repository = _unitOfWork.Repository<AppSetting>();
            var keys = updates.Select(u => u.Key.Trim()).ToList();
            var existing = await repository.Query()
                .Where(s => keys.Contains(s.Key))
                .ToDictionaryAsync(s => s.Key, cancellationToken);

            foreach (var update in updates)
            {
                var key = update.Key.Trim();
                if (key.Length == 0)
                {
                    throw new DomainValidationException("Setting keys cannot be empty.");
                }

                if (existing.TryGetValue(key, out var setting))
                {
                    setting.Value = update.Value;
                    repository.Update(setting);
                }
                else
                {
                    await repository.AddAsync(
                        new AppSetting
                        {
                            Key = key,
                            Value = update.Value,
                            Category = update.Category,
                            IsPublic = update.IsPublic,
                        },
                        cancellationToken);
                }
            }

            await _auditLog.StageAsync(
                AuditAction.Update,
                nameof(AppSetting),
                string.Join(",", keys),
                cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return await GetAllAsync(cancellationToken);
        }

        private static SettingDto ToDto(AppSetting setting) => new()
        {
            Category = setting.Category,
            Key = setting.Key,
            Value = setting.Value,
            IsPublic = setting.IsPublic,
        };
    }
}
