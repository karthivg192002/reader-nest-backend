using iucs.readernest.domain.Entities.Users;

namespace iucs.readernest.application.Common.Interfaces
{
    public class TokenResult
    {
        public string AccessToken { get; set; } = null!;

        public DateTime ExpiresAtUtc { get; set; }
    }

    /// <summary>
    /// Issues signed access tokens. Implemented in the API layer (JWT) so the
    /// application layer stays free of ASP.NET dependencies.
    /// </summary>
    public interface ITokenService
    {
        TokenResult CreateToken(User user, IReadOnlyCollection<string> permissionClaims);
    }
}
