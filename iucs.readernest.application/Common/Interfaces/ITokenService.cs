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

        /// <summary>
        /// A shareable "Guest Link" bridge token (see SessionService.CreateGuestLinkAsync /
        /// GetGuestJoinAsync) — opaque and signed, carries just enough (a "purpose" claim, the
        /// session id, and an optional child id) to resolve back to a live Jitsi join without a
        /// logged-in user behind it. Never registered with the normal ASP.NET auth pipeline the
        /// way a user's access token is — <see cref="ValidateGuestJoinToken"/> parses it
        /// directly, since the guest-join endpoint is anonymous by design and this token is
        /// never meant to authenticate into anything else. <paramref name="expiresAtUtc"/> is
        /// only a generous outer safety bound so an unused link doesn't stay mintable forever;
        /// the real "does this link still work" gate is GetGuestJoinAsync's own live check of
        /// the session's current status/time on every open.
        /// </summary>
        TokenResult CreateGuestJoinToken(Guid sessionId, Guid? childId, DateTime expiresAtUtc);

        /// <summary>
        /// Parses/validates a token minted by <see cref="CreateGuestJoinToken"/>. Returns null
        /// for anything invalid, expired, or not actually a guest-join token (wrong "purpose"
        /// claim) — SessionService.GetGuestJoinAsync treats all of those identically to "this
        /// link doesn't exist."
        /// </summary>
        (Guid SessionId, Guid? ChildId)? ValidateGuestJoinToken(string token);
    }
}
