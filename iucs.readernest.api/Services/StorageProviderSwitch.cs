using iucs.readernest.application.Common;
using iucs.readernest.domain.Entities.Settings;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Which store new uploads go to right now: S3 or this server's disk. Chosen per client on
    /// Admin → Settings → File storage and kept in the database ("storage.provider" AppSetting), so
    /// it switches live without a redeploy; until that row exists, Storage:Provider in configuration
    /// decides. Without S3 credentials it is always Local -- there is nowhere else to put files.
    /// </summary>
    public class StorageProviderSwitch
    {
        private volatile bool _local;

        public StorageProviderSwitch(IConfiguration configuration)
        {
            _local = IsLocal(configuration["Storage:Provider"]);
            S3Configured = S3FileStorage.IsConfigured(configuration);
        }

        public bool S3Configured { get; }

        public bool UseLocal => _local || !S3Configured;

        /// <summary>"S3" or "Local" -- the store actually in use.</summary>
        public string Provider => UseLocal ? StorageSettings.Local : StorageSettings.S3;

        /// <summary>Picks up the saved choice at startup (after the database is ready).</summary>
        public async Task LoadAsync(IServiceProvider services, CancellationToken cancellationToken = default)
        {
            using var scope = services.CreateScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var saved = await unitOfWork.Repository<AppSetting>().Query()
                .Where(s => s.Key == StorageSettings.ProviderKey)
                .Select(s => s.Value)
                .FirstOrDefaultAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(saved))
            {
                _local = IsLocal(saved);
            }
        }

        /// <summary>Saves the choice and applies it at once (the caller stages the audit entry).</summary>
        public async Task SetAsync(bool local, IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
        {
            var value = local ? StorageSettings.Local : StorageSettings.S3;
            var repository = unitOfWork.Repository<AppSetting>();
            var setting = await repository.FirstOrDefaultAsync(s => s.Key == StorageSettings.ProviderKey, cancellationToken);
            if (setting is null)
            {
                await repository.AddAsync(
                    new AppSetting { Key = StorageSettings.ProviderKey, Value = value, Category = SettingCategory.General },
                    cancellationToken);
            }
            else
            {
                setting.Value = value;
                repository.Update(setting);
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            _local = local;
        }

        private static bool IsLocal(string? provider) =>
            string.Equals(provider?.Trim(), StorageSettings.Local, StringComparison.OrdinalIgnoreCase);
    }
}
