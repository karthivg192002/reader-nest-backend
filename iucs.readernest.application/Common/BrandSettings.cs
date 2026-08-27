using iucs.readernest.domain.Entities.Settings;
using iucs.readernest.domain.Repository;

namespace iucs.readernest.application.Common
{
    /// <summary>
    /// Reads the admin's Settings &amp; Branding "Brand name" (brand.name AppSetting) — the
    /// same value the frontend's reactive useBrand() reads for page title/logo/UI text — for
    /// the few places backend-rendered content needs the org's display name (the Razorpay
    /// checkout popup, an email's fallback subject). Mirrors NotificationToggles' own
    /// single-key AppSetting read.
    /// </summary>
    public static class BrandSettings
    {
        public const string NameKey = "brand.name";
        public const string DefaultName = "The Reader Nest";

        public static async Task<string> GetNameAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
        {
            var setting = await unitOfWork.Repository<AppSetting>()
                .FirstOrDefaultAsync(s => s.Key == NameKey, cancellationToken);
            return string.IsNullOrWhiteSpace(setting?.Value) ? DefaultName : setting.Value.Trim();
        }
    }
}
