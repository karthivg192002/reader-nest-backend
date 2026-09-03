namespace iucs.readernest.application.Services
{
    public interface IShortLinkService
    {
        Task<string> CreateAsync(string targetUrl, DateTime expiresAtUtc, Guid createdByUserId, CancellationToken cancellationToken = default);

        /// <summary>Null when the slug doesn't exist or has expired.</summary>
        Task<string?> ResolveAsync(string slug, CancellationToken cancellationToken = default);
    }
}
