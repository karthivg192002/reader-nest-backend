namespace iucs.readernest.application.Dto.Sessions
{
    /// <summary>
    /// One enrolled student, for the "Copy Guest Link" student picker (subadmin/admin/
    /// coordinator Sessions screens). Deliberately served off SessionCalendarManagement:View
    /// (see SessionsController's guest-link routes) rather than requiring
    /// CourseBatchManagement:View too — an RM with only session access shouldn't 403 just for
    /// this popup, the same reasoning subadmin/Sessions.tsx already documents for its own
    /// batch-name lookups.
    /// </summary>
    public class GuestLinkStudentDto
    {
        public Guid ChildId { get; set; }

        public string ChildName { get; set; } = null!;

        public string ParentName { get; set; } = null!;

        public string? ParentPhone { get; set; }
    }

    /// <summary>Pass ChildId to bind the Guest Link to one specific enrolled student (auto
    /// attendance, skips prejoin); omit it for a generic guest link (manual prejoin, no auto
    /// attendance — the teacher marks that student present by hand instead).</summary>
    public class CreateGuestLinkRequest
    {
        public Guid? ChildId { get; set; }
    }

    /// <summary>
    /// The opaque, signed bridge token a "Copy Guest Link" action hands back — the frontend
    /// builds the shareable URL from it (e.g. "/guest-join?token=..."), it is never shown to
    /// the RM directly. See JwtTokenService.CreateGuestJoinToken: its own expiry is a generous
    /// outer safety bound only, not the real "is this link still good" gate — GetGuestJoinAsync
    /// re-checks the session's LIVE status/time on every open instead, which is what lets the
    /// same link be reused any number of times right up until the class actually ends (or is
    /// marked Completed early), rather than dying on a fixed schedule.
    /// </summary>
    public class GuestLinkDto
    {
        public string Token { get; set; } = null!;
    }

    /// <summary>Body for the anonymous guest-join landing call.</summary>
    public class GuestJoinRequest
    {
        public string Token { get; set; } = null!;
    }

    /// <summary>Everything the anonymous "/guest-join" landing page needs to hand a caretaker
    /// straight into the Jitsi room, plus enough for the controller to know whether to also
    /// mark attendance (see SessionsController.GuestJoin).</summary>
    public class GuestJoinDto
    {
        public Guid SessionId { get; set; }

        /// <summary>Set only for a student-bound link whose enrollment was still valid at open
        /// time — the controller uses this to decide whether to call
        /// IAcademicOpsService.CaptureGuestJoinAttendanceAsync.</summary>
        public Guid? ChildId { get; set; }

        public string Room { get; set; } = null!;

        public string Domain { get; set; } = null!;

        /// <summary>Null when the deployment hasn't been configured for token-verified joins yet — same caveat as JitsiJoinDto.Token.</summary>
        public string? Token { get; set; }

        /// <summary>Pre-filled with the enrolled child's name for a student-bound link; "Guest" for a generic one.</summary>
        public string DisplayName { get; set; } = null!;

        /// <summary>True for a student-bound link — the landing page skips its own name-entry
        /// step entirely and joins straight in with DisplayName already set. False for a
        /// generic guest link, which asks the opener to type their own name first.</summary>
        public bool SkipPrejoin { get; set; }

        /// <summary>
        /// ClassroomHub-only token (see JwtTokenService.CreateGuestClassroomHubToken) — lets the
        /// guest-join landing page render the full in-app interactive classroom (whiteboard,
        /// quiz, roster, gamification) via JitsiLive's `hubToken` prop, the same mechanism
        /// Jibri's recording-observer page already uses, rather than dropping the caretaker into
        /// a bare Jitsi call with none of it. Unlike <see cref="Token"/> (the Jitsi join token,
        /// which needs the "jitsi" Integration's appId/appSecret configured), this is signed with
        /// this app's own always-present JWT key, so it's never null.
        /// </summary>
        public string HubToken { get; set; } = null!;
    }
}
