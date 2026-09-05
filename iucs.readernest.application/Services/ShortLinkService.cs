using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Repository;

namespace iucs.readernest.application.Services
{
    public class ShortLinkService : IShortLinkService
    {
        // Excludes visually-ambiguous characters (0/O, 1/I/l) -- these get read aloud or
        // retyped by hand often enough (someone copying a link off a phone screen) that a
        // slug should never depend on telling them apart. 62^7-ish alphabet minus a handful
        // is still ample keyspace for a link that lives a few hours.
        private const string Alphabet = "23456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghjkmnpqrstuvwxyz";
        private const int SlugLength = 7;
        private const int MaxCollisionRetries = 5;

        private readonly IUnitOfWork _unitOfWork;

        public ShortLinkService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<string> CreateAsync(string targetUrl, DateTime expiresAtUtc, Guid createdByUserId, CancellationToken cancellationToken = default)
        {
            var repository = _unitOfWork.Repository<ShortLink>();
            for (var attempt = 0; attempt < MaxCollisionRetries; attempt++)
            {
                var slug = GenerateSlug();
                if (await repository.ExistsAsync(s => s.Slug == slug, cancellationToken))
                {
                    continue;
                }

                await repository.AddAsync(
                    new ShortLink
                    {
                        Slug = slug,
                        TargetUrl = targetUrl,
                        CreatedByUserId = createdByUserId,
                        ExpiresAtUtc = expiresAtUtc,
                    },
                    cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
                return slug;
            }

            // Astronomically unlikely at this keyspace/table size, but a hard failure here
            // beats an infinite loop or a silently-wrong slug.
            throw new InvalidOperationException("Could not generate a unique short link after several attempts.");
        }

        public async Task<string?> ResolveAsync(string slug, CancellationToken cancellationToken = default)
        {
            var link = await _unitOfWork.Repository<ShortLink>()
                .FirstOrDefaultAsync(s => s.Slug == slug, cancellationToken);
            return link is null || link.ExpiresAtUtc < DateTime.UtcNow ? null : link.TargetUrl;
        }

        private static string GenerateSlug()
        {
            Span<char> chars = stackalloc char[SlugLength];
            for (var i = 0; i < SlugLength; i++)
            {
                chars[i] = Alphabet[Random.Shared.Next(Alphabet.Length)];
            }
            return new string(chars);
        }
    }
}
