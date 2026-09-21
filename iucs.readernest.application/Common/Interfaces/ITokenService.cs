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
        /// CreateGuestLinkForParticipantAsync / GetGuestJoinAsync) — opaque and signed, carries
        /// just enough (a "purpose" claim, the session id, and either a child id or a raw
        /// name/email pair) to resolve back to a live Jitsi join without a logged-in user behind
        /// it. Never registered with the normal ASP.NET auth pipeline the way a user's access
        /// token is — <see cref="ValidateGuestJoinToken"/> parses it directly, since the
        /// guest-join endpoint is anonymous by design and this token is never meant to
        /// authenticate into anything else. <paramref name="expiresAtUtc"/> is only a generous
        /// outer safety bound for a <paramref name="childId"/>-bound link so an unused one
        /// doesn't stay mintable forever; the real "does this link still work" gate is
        /// GetGuestJoinAsync's own live check of the session's current status/time on every
        /// open. <paramref name="guestName"/>/<paramref name="guestEmail"/> are for a participant
        /// with no Child/BatchEnrollment row to resolve a name from at all (a Demo lead, via
        /// DemoBookingService) — mutually exclusive with <paramref name="childId"/> in practice
        /// (a caller sets at most one), and GetGuestJoinAsync deliberately skips its own
        /// expiry/live-status gate for this shape of link, matching the Demo join redirect's own
        /// long-standing "never expires, still works weeks later" contract it replaces.
        /// </summary>
        TokenResult CreateGuestJoinToken(Guid sessionId, Guid? childId, DateTime expiresAtUtc, string? guestName = null, string? guestEmail = null);

        /// <summary>
        /// Parses/validates a token minted by <see cref="CreateGuestJoinToken"/>. Returns null
        /// for anything invalid, expired, or not actually a guest-join token (wrong "purpose"
        /// claim) — SessionService.GetGuestJoinAsync treats all of those identically to "this
        /// link doesn't exist."
        /// </summary>
        (Guid SessionId, Guid? ChildId, string? GuestName, string? GuestEmail)? ValidateGuestJoinToken(string token);

        /// <summary>
        /// A ClassroomHub-only token for a Guest Link join (see SessionService.GetGuestJoinAsync
        /// and JitsiLive.tsx's `hubToken` prop) — same shape/purpose as
        /// <see cref="CreateRecordingObserverHubToken"/> (a throwaway subject, scoped to one
        /// <paramref name="sessionId"/> via claims Program.cs's OnTokenValidated and
        /// ClassroomHub.JoinSession both special-case), but for a real caretaker rejoining the
        /// interactive classroom as an ordinary student participant rather than Jibri's headless
        /// observer: the room roster, whiteboard, quiz and gamification all need this to behave
        /// like a normal join, not a silent one. <paramref name="participantName"/> is the same
        /// name GetGuestJoinAsync already resolved (the enrolled child's name, or "Guest").
        /// </summary>
        TokenResult CreateGuestClassroomHubToken(Guid sessionId, Guid? childId, string participantName, DateTime expiresAtUtc);
    }
}
