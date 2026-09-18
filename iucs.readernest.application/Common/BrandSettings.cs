using System.Runtime.CompilerServices;
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
        public const string DefaultName = "Meet to Manage";

        // Fan-out sends (e.g. one notification email per recipient in a batch) call this once
        // per recipient using the same request-scoped IUnitOfWork — a fresh SELECT per call
        // was a real N+1. Cached per-IUnitOfWork (via a weak table so it never outlives the
        // scoped instance it's keyed on) rather than process-wide: IUnitOfWork is Scoped/
        // per-request, so this dies with the request automatically — no TTL or cross-request
        // invalidation to get wrong, and no risk of one request seeing another's brand-name
        // update or of the value going stale across an admin edit.
        private static readonly ConditionalWeakTable<IUnitOfWork, StrongBox<string>> Cache = new();

        public static async Task<string> GetNameAsync(IUnitOfWork unitOfWork, CancellationToken cancellationToken = default)
        {
            if (Cache.TryGetValue(unitOfWork, out var box))
            {
                return box.Value!;
            }

            var setting = await unitOfWork.Repository<AppSetting>()
                .FirstOrDefaultAsync(s => s.Key == NameKey, cancellationToken);
            var name = string.IsNullOrWhiteSpace(setting?.Value) ? DefaultName : setting.Value.Trim();
            Cache.AddOrUpdate(unitOfWork, new StrongBox<string>(name));
            return name;
        }
    }
}
