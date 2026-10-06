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
        /// <summary>Mirrors AppSetting.Key's [MaxLength(100)] — validated here so an over-long key is a 400, not a DbUpdateException.</summary>
        private const int MaxKeyLength = 100;

        /// <summary>Mirrors AppSetting.Value's [MaxLength(2000)].</summary>
        private const int MaxValueLength = 2000;

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
            var keys = updates.Select(u => u.Key?.Trim() ?? string.Empty).ToList();

            // Validate the whole payload before touching the repository: this is a bulk
            // upsert, so a key that only fails at SaveChanges would take the other settings
            // in the same request down with it as an opaque 500.
            foreach (var key in keys)
            {
                if (key.Length == 0)
                {
                    throw new DomainValidationException("Setting keys cannot be empty.");
                }

                // AppSetting.Key/Value are varchar(100)/varchar(2000); UpdateSettingRequest
                // carries no length attributes of its own, so an over-long key or value would
                // otherwise pass model validation and fail as a DbUpdateException instead.
                if (key.Length > MaxKeyLength)
                {
                    throw new DomainValidationException(
                        $"Setting key '{key[..Math.Min(key.Length, 32)]}…' exceeds {MaxKeyLength} characters.");
                }

                // Switching storage takes effect live and has checks of its own (StorageController).
                if (string.Equals(key, Common.StorageSettings.ProviderKey, StringComparison.OrdinalIgnoreCase))
                {
                    throw new DomainValidationException("Change where files are stored from Settings → File storage.");
                }
            }

            var overLongValue = updates.FirstOrDefault(u => u.Value is not null && u.Value.Length > MaxValueLength);
            if (overLongValue is not null)
            {
                throw new DomainValidationException(
                    $"Value for setting '{overLongValue.Key?.Trim()}' exceeds {MaxValueLength} characters.");
            }

            // Key is uniquely indexed, so the same key twice in one payload inserts two rows
            // that collide at SaveChanges — a 500 for what is plainly a malformed request.
            // Rejecting it (rather than silently letting the last one win) matches how
            // RoleService.MapPermissions handles a duplicated module in the same situation.
            var duplicateKey = keys.GroupBy(k => k).FirstOrDefault(g => g.Count() > 1);
            if (duplicateKey is not null)
            {
                throw new DomainValidationException($"Setting key '{duplicateKey.Key}' appears more than once.");
            }

            var existing = await repository.Query()
                .Where(s => keys.Contains(s.Key))
                .ToDictionaryAsync(s => s.Key, cancellationToken);

            foreach (var update in updates)
            {
                var key = update.Key.Trim();

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

            var entityId = keys.Count == 1 ? keys[0] : $"{keys.Count} keys";
            if (entityId.Length > 64)
            {
                entityId = entityId[..64];
            }

            await _auditLog.StageAsync(
                AuditAction.Update,
                nameof(AppSetting),
                entityId,
                changesJson: System.Text.Json.JsonSerializer.Serialize(keys),
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
