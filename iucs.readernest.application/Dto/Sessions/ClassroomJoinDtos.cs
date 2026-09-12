namespace iucs.readernest.application.Dto.Sessions
{
    /// <summary>Everything the live-classroom screen needs to embed the Jitsi room for one caller.</summary>
    public class JitsiJoinDto
    {
        public string Room { get; set; } = null!;

        public string Domain { get; set; } = null!;

        /// <summary>Null when the deployment hasn't been configured for token-verified joins yet.</summary>
        public string? Token { get; set; }

        /// <summary>The session's scheduled end — lets the classroom screen warn the teacher when
        /// time's up. Nothing about the call itself is cut off at this time (see
        /// Docs/LONG_DURATION_SESSIONS.md); it's advisory only.</summary>
        public DateTime ScheduledEndAtUtc { get; set; }
    }

    /// <summary>
    /// Everything Jibri's headless "recording observer" page needs to join the same call and
    /// ClassroomHub session a real teacher would see — see docs/JITSI_ARCHITECTURE.md's
    /// recording-observer section and SessionService.GetLiveObserverJoinAsync.
    /// </summary>
    public class RecordingObserverJoinDto
    {
        public Guid SessionId { get; set; }

        public string Room { get; set; } = null!;

        public string Domain { get; set; } = null!;

        /// <summary>Null when the deployment hasn't been configured for token-verified joins yet — same caveat as JitsiJoinDto.Token.</summary>
        public string? Token { get; set; }

        /// <summary>Bearer token for ClassroomHub only (see CreateRecordingObserverHubToken) — not the same signing key/audience as <see cref="Token"/>, which is Jitsi's own.</summary>
        public string? HubToken { get; set; }

        public DateTime ExpiresAtUtc { get; set; }
    }

    /// <summary>Non-secret Jitsi settings the classroom screen needs before it joins.</summary>
    public class ClassroomSettingsDto
    {
        public string Domain { get; set; } = null!;

        public bool AutoRecordEnabled { get; set; }

        /// <summary>Whether a class should start with Jitsi's lobby (waiting room) already on, so a
        /// student can't land straight in an unattended room before the teacher's even joined —
        /// admin-configurable (Settings → Integrations) rather than always off, which is what a
        /// teacher had to remember to turn on by hand, every single class, for it to apply at all.</summary>
        public bool DefaultLobbyEnabled { get; set; }
    }
}
