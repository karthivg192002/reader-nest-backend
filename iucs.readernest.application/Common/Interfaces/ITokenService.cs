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

        /// <summary>
        /// A ClassroomHub-only token for the anonymous Jibri "recording observer" page (see
        /// docs/JITSI_ARCHITECTURE.md's recording-observer section): no real <see cref="User"/>
        /// backs it, so it carries a throwaway subject id that never resolves to a DB row.
        /// Scoped to exactly one <paramref name="sessionId"/> via a "sessionId" claim and a
        /// "purpose: recording-observer" claim — both <c>Program.cs</c>'s <c>OnTokenValidated</c>
        /// and <c>ClassroomHub.JoinSession</c> special-case this purpose instead of doing the
        /// normal active-user/participant checks, which would otherwise reject it outright.
        /// </summary>
        TokenResult CreateRecordingObserverHubToken(Guid sessionId, DateTime expiresAtUtc);
    }
}
