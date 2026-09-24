namespace iucs.readernest.application.Dto.Sessions
{
    /// <summary>Everything the live-classroom screen needs to embed the Jitsi room for one caller.</summary>
    public class JitsiJoinDto
    {
        /// <summary>The session this join is actually for — echoes back the id the caller asked
        /// for, UNLESS that one has since been rescheduled (edited), in which case this is
        /// whatever it turned into (walked forward through however many reschedules deep).
        /// The caller MUST switch to using this id for anything else session-scoped for the rest
        /// of this call (the ClassroomHub join, engagement posts, complete-session, recording
        /// registration) — using the original id there is exactly the bug this field exists to
        /// prevent: a teacher and a student each holding a different-but-equally-"valid" id for
        /// what they think is the same class end up in two different ClassroomHub groups (their
        /// own People roster and whiteboard never syncing with the other), even though both land
        /// in the same Jitsi video room (MeetingRoomId carries over on a reschedule) and so the
        /// call itself looks completely fine to both of them.</summary>
        public Guid SessionId { get; set; }

        public string Room { get; set; } = null!;

        public string Domain { get; set; } = null!;

        /// <summary>Null when the deployment hasn't been configured for token-verified joins yet.</summary>
        public string? Token { get; set; }

        /// <summary>The session's scheduled end — lets the classroom screen warn the teacher when
        /// time's up. Nothing about the call itself is cut off at this time (see
        /// Docs/LONG_DURATION_SESSIONS.md); it's advisory only.</summary>
        public DateTime ScheduledEndAtUtc { get; set; }

        /// <summary>The caller's name as signed into the Jitsi token (what Jitsi shows for them,
        /// including on the lobby's knock).</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>True when an admin-team member (Admin, Sub Admin, Admission) is joining as a
        /// monitor — the host's classroom admits them from the lobby automatically.</summary>
        public bool IsMonitor { get; set; }

        /// <summary>True for a Demo-type session (demos are auto-recorded like any other class).</summary>
        public bool IsDemo { get; set; }

        /// <summary>Jitsi display names (the same "First Last" the join token carries) of every
        /// active Admin / Sub Admin (RM, Coordinator, Counselor, Management) / Admission user.
        /// Only filled in for a Teacher/Admin caller — the classroom screen auto-admits a
        /// waiting-room knock from any of these names so staff can walk into a class without the
        /// teacher having to let them in. Empty for everyone else.</summary>
        public List<string> StaffNames { get; set; } = new();
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
