namespace iucs.readernest.application.Common.Interfaces
{
    /// <summary>
    /// Mints a room-scoped Jitsi join token so the app — not a bare, forever-guessable room
    /// name — decides who can enter a live class's video call. Implemented in the API layer
    /// (needs JWT signing libraries the application layer deliberately doesn't reference).
    /// </summary>
    public interface IJitsiTokenService
    {
        /// <summary>
        /// Returns null when the "jitsi" Integration has no appId/appSecret configured yet
        /// (today's default — see docs/JITSI_ARCHITECTURE.md's "Production hardening (Sprint 2)"
        /// item): callers fall back to an unsigned join, identical to current behavior. Once
        /// configured, the token is scoped to <paramref name="room"/> only and expires at
        /// <paramref name="expiresAtUtc"/> — it grants no access to any other class or beyond
        /// that class's window.
        /// </summary>
        string? CreateToken(
            string domain,
            string? jitsiConfigJson,
            string room,
            string participantName,
            string? participantEmail,
            bool moderator,
            DateTime expiresAtUtc);

        /// <summary>
        /// Validates a Jibri finalize-recording bearer token: same appId/appSecret as room-join
        /// tokens (signature, issuer, audience, expiry), but additionally requires a
        /// <c>purpose: "recording-finalize"</c> claim — so a leaked/logged room-join token can't
        /// be replayed here — and the token's own <c>room</c> claim must equal
        /// <paramref name="expectedRoom"/>, so a token minted for one room can't register a
        /// recording under another. Returns false for anything else, including no "jitsi"
        /// Integration configured at all (there's nothing to verify a signature against).
        /// </summary>
        bool ValidateFinalizeToken(string? bearerToken, string? jitsiConfigJson, string expectedRoom);
    }
}
