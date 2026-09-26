using iucs.readernest.domain.Entities.Settings;
using iucs.readernest.domain.Repository;

namespace iucs.readernest.application.Common
{
    /// <summary>
    /// Admin-tunable admission and class policies (Settings -> Notifications tab, "admission.*" and
    /// "parent.*" AppSettings). A missing or invalid key falls back to the documented default, so a
    /// centre that has never touched the setting behaves as documented here.
    /// </summary>
    public static class PolicySettings
    {
        public const string TermsUrlKey = "admission.termsUrl";
        public const string TermsTextKey = "admission.termsText";
        public const string CancelCutoffMinutesKey = "parent.cancelCutoffMinutes";

        /// <summary>A parent may cancel/apply for leave until this many minutes before the class starts.</summary>
        public const int DefaultCancelCutoffMinutes = 60;

        public static async Task<int> GetCancelCutoffMinutesAsync(
            IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
        {
            var setting = await unitOfWork.Repository<AppSetting>()
                .FirstOrDefaultAsync(s => s.Key == CancelCutoffMinutesKey, cancellationToken);
            return setting?.Value is { } raw && int.TryParse(raw, out var parsed) && parsed >= 0
                ? parsed
                : DefaultCancelCutoffMinutes;
        }

        public static async Task<string?> GetAsync(
            IUnitOfWork unitOfWork, string key, CancellationToken cancellationToken = default)
        {
            var setting = await unitOfWork.Repository<AppSetting>()
                .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
            return string.IsNullOrWhiteSpace(setting?.Value) ? null : setting!.Value!.Trim();
        }
    }
}
