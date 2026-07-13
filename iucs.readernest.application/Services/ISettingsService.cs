using iucs.readernest.application.Dto.Settings;

namespace iucs.readernest.application.Services
{
    public interface ISettingsService
    {
        /// <summary>Every setting row, for the admin Settings &amp; Branding screen.</summary>
        Task<IReadOnlyList<SettingDto>> GetAllAsync(CancellationToken cancellationToken = default);

        /// <summary>Public rows only (branding); safe to serve before login.</summary>
        Task<IReadOnlyList<SettingDto>> GetPublicAsync(CancellationToken cancellationToken = default);

        /// <summary>Bulk upsert with replace-by-key semantics; the screen submits only changed keys.</summary>
        Task<IReadOnlyList<SettingDto>> UpsertAsync(
            IReadOnlyList<UpdateSettingRequest> updates,
            CancellationToken cancellationToken = default);
    }
}
