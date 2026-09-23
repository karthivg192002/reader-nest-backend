using iucs.readernest.application.Dto.Sessions;

namespace iucs.readernest.application.Services
{
    public interface ISessionService
    {
        Task<IReadOnlyList<ClassSessionDto>> ListAsync(
            DateTime fromUtc,
            DateTime toUtc,
            Guid? teacherProfileId,
            Guid? batchId,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ClassSessionDto>> ListForTeacherUserAsync(
            Guid userId,
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken cancellationToken = default);

        Task<ClassSessionDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

        Task<ClassSessionDto> ScheduleAsync(ScheduleSessionRequest request, CancellationToken cancellationToken = default);

        Task<ClassSessionDto> RescheduleAsync(Guid id, RescheduleSessionRequest request, CancellationToken cancellationToken = default);

        Task<ClassSessionDto> CancelAsync(Guid id, CancelSessionRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// A parent cancels their own child's upcoming class from the portal — immediately, with
        /// no admin approval — and the class's teacher is emailed that the parent cancelled and
        /// why. Only for a class that is the family's alone (not a shared group batch).
        /// </summary>
        /// <summary>
        /// System-only completion (no caller check) for a class the teacher left without pressing
        /// End Class and never rejoined — ended at <paramref name="teacherLeftAtUtc"/>, so a class
        /// cut short is flagged for payout approval like any other. Only for
        /// <c>AbandonedClassCompletionBackgroundService</c>; never exposed on a controller.
        /// </summary>
        Task<ClassSessionDto> CompleteAbandonedAsync(Guid id, DateTime teacherLeftAtUtc, CancellationToken cancellationToken = default);

        Task<ClassSessionDto> CancelByParentAsync(Guid parentUserId, Guid sessionId, string reason, CancellationToken cancellationToken = default);

        /// <summary>
        /// Marks a session completed and, when all course sessions of the batch are done,
        /// automatically moves the batch to Dormant (course completion tracking).
        /// </summary>
        Task<ClassSessionDto> CompleteAsync(
            Guid id,
            CompleteSessionRequest? request = null,
            CancellationToken cancellationToken = default);

        /// <summary>Edits a completed session's notes after the fact — see the implementation's
        /// own doc comment for why this exists separately from CompleteAsync.</summary>
        Task<ClassSessionDto> UpdateSummaryAsync(
            Guid id,
            string summary,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ClassSessionDto>> GenerateScheduleAsync(
            Guid batchId,
            GenerateScheduleRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Edits a batch's schedule from now on (new weekday/time pattern and/or remaining
        /// session count) without re-entering the whole plan or touching sessions already
        /// completed/in progress — see the implementation's own doc comment for the "Manage"
        /// dialog gap this replaces.
        /// </summary>
        Task<IReadOnlyList<ClassSessionDto>> UpdateFutureScheduleAsync(
            Guid batchId,
            UpdateFutureScheduleRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Marks a no-show: the session is flagged, a carried-forward replacement is
        /// scheduled, and the payout impact accrues (waiting amount for a student
        /// no-show, deduction plus admin alert for a teacher no-show).
        /// </summary>
        Task<ClassSessionDto> MarkNoShowAsync(Guid id, MarkNoShowRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// System-initiated equivalent of <see cref="MarkNoShowAsync"/> for
        /// <c>NoShowDetectionBackgroundService</c>: identical carry-forward/payout behaviour,
        /// but skips the caller-ownership check since there is no signed-in caller. Not exposed
        /// on any controller.
        /// </summary>
        Task<ClassSessionDto> MarkNoShowSystemAsync(Guid id, NoShowParty party, string note, CancellationToken cancellationToken = default);

        /// <summary>
        /// Called by <c>NoShowDetectionBackgroundService</c> for an overdue Demo session that has
        /// no DemoBooking linked to it at all — as opposed to one that exists but nobody joined.
        /// No student was ever going to attend such a slot, so it is a misconfigured/orphaned
        /// booking, not a genuine no-show: this does not touch the session's status or accrue any
        /// payout (which <see cref="MarkNoShowSystemAsync"/> would do), it only alerts an admin so
        /// a human can Close/Mark Holiday/Reschedule it from the calendar.
        /// </summary>
        Task FlagOrphanedDemoSessionAsync(Guid id, CancellationToken cancellationToken = default);

        Task<SessionRecordingDto> AddRecordingAsync(
            Guid sessionId,
            RegisterRecordingRequest request,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<SessionRecordingDto>> ListRecordingsAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default);

        /// <summary>Admin-wide (teacherUserId null) or one teacher's own (teacherUserId set)
        /// Recordings page — every registered recording in scope, paged, optionally filtered to
        /// one calendar date (by the class's own ScheduledStartAtUtc).</summary>
        Task<iucs.readernest.application.Dto.Common.PagedResult<RecordingListItemDto>> ListAllRecordingsAsync(
            int page,
            int pageSize,
            DateOnly? date,
            Guid? teacherUserId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Removes a recording's record (Admin only — see <see cref="Common.Exceptions.ForbiddenException"/>).
        /// Only unregisters it here; the underlying file in storage is left untouched, since
        /// <c>IFileStorage</c> has no delete operation and recordings may live in storage the
        /// Jibri pipeline writes to directly rather than through this app's own upload path.
        /// </summary>
        Task DeleteRecordingAsync(
            Guid sessionId,
            Guid recordingId,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Machine-to-machine equivalent of <see cref="AddRecordingAsync"/> for the Jibri
        /// finalize-recording hook: no signed-in caller, so <paramref name="bearerToken"/> (a
        /// short-lived JWT signed with the same appId/appSecret as room-join tokens) is the
        /// authorization instead of <c>EnsureSessionParticipantAsync</c> — see
        /// <see cref="Common.Interfaces.IJitsiTokenService.ValidateFinalizeToken"/>. Returns null
        /// (not an error) when <paramref name="roomName"/> doesn't match any ClassSession (e.g.
        /// a personal or demo room) — Jibri records those too, but there's nothing in our data
        /// model to attach the recording to.
        /// </summary>
        Task<SessionRecordingDto?> FinalizeJibriRecordingAsync(
            string roomName,
            string? bearerToken,
            string storageUrl,
            int? durationSeconds,
            CancellationToken cancellationToken = default);

        /// <summary>Engagement tracking: batches of quiz/activity/whiteboard/attention signals from the live classroom.</summary>
        Task RecordEngagementAsync(Guid sessionId, RecordEngagementRequest request, CancellationToken cancellationToken = default);

        /// <summary>Per-participant engagement scores and learning outcome indicators.</summary>
        Task<IReadOnlyList<EngagementSummaryDto>> GetEngagementSummaryAsync(Guid sessionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Whether the given user genuinely belongs to this session — Admin always,
        /// the specific assigned teacher, or a parent with a child enrolled in the
        /// session's batch. Used to gate access to the live classroom (ClassroomHub)
        /// and any other entry point that must confirm real participation, not just
        /// "is a valid logged-in user of some role."
        /// </summary>
        Task<bool> IsSessionParticipantAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Walks a session forward through however many reschedules deep to whatever it's
        /// actually become — returns `sessionId` itself when it was never rescheduled (the
        /// overwhelmingly common case) or no longer exists (lets the caller's own not-found
        /// handling fire normally). See ClassroomHub.JoinSession's own call site: a connection
        /// that still has a pre-edit session id (e.g. a portal tab that was already open when
        /// an admin edited the session, and never reloaded) resolves here to the same current
        /// session id GetJitsiJoinAsync's own resolution would give a fresh caller, so both
        /// land in the same ClassroomHub group regardless of which id either of them started
        /// from — the underlying bug being defended against in both places is the same one:
        /// two people in the same live class, differing only in which one already had a stale
        /// id cached, ending up unable to see each other in People or on the shared whiteboard.
        /// </summary>
        Task<Guid> ResolveCurrentSessionIdAsync(Guid sessionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// The room + (when configured) a signed, room-scoped join token for the caller.
        /// Authorized the same way as the ClassroomHub: Admin, the assigned teacher, or a
        /// parent with a child enrolled in the session's batch — anyone else is refused
        /// before a token is ever minted.
        /// </summary>
        Task<JitsiJoinDto> GetJitsiJoinAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Machine-to-machine equivalent of <see cref="GetJitsiJoinAsync"/> for Jibri's headless
        /// "recording observer" page: no signed-in caller, so authorization is "does an
        /// InProgress ClassSession exist for this exact room right now" rather than
        /// IsSessionParticipantAsync. Returns null (not an error) when there's no such session —
        /// a personal room Jibri is recording ad hoc, or a startup race before the ClassSession
        /// flips to InProgress — mirroring FinalizeJibriRecordingAsync's "not every room maps to
        /// a trackable moment" no-op philosophy.
        /// </summary>
        Task<RecordingObserverJoinDto?> GetLiveObserverJoinAsync(string roomName, CancellationToken cancellationToken = default);

        /// <summary>Non-secret Jitsi settings (domain, auto-record) for whoever is about to join a live class.</summary>
        Task<ClassroomSettingsDto> GetClassroomSettingsAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Attaches (replacing any prior one) the PDF deck the teacher wants to present live in
        /// this class. Teacher-only — the assigned teacher or Admin, same gate as
        /// <see cref="IsSessionParticipantAsync"/>'s moderator check, not every participant.
        /// </summary>
        Task<SessionPresentationDto> UploadPresentationAsync(
            Guid sessionId,
            Guid userId,
            string storageUrl,
            string originalFileName,
            CancellationToken cancellationToken = default);

        /// <summary>Metadata only (no storage path) — null if nothing's been uploaded for this session yet.</summary>
        Task<SessionPresentationDto?> GetPresentationAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default);

        /// <summary>Resolves the stored file for download — same participant gate as viewing metadata.</summary>
        Task<SessionPresentationDownloadDto> GetPresentationForDownloadAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default);

        /// <summary>The session's batch roster for the "Copy Guest Link" student picker — see
        /// GuestLinkStudentDto for why this is scoped off the session's own permission rather
        /// than requiring CourseBatchManagement:View too. Empty for a Demo (no batch).</summary>
        Task<IReadOnlyList<GuestLinkStudentDto>> GetGuestLinkStudentsAsync(Guid sessionId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Mints a shareable Guest Link token for one session (see JwtTokenService.
        /// CreateGuestJoinToken). Pass <paramref name="childId"/> to bind the link to one
        /// specific, currently-active enrollment of the session's batch (auto attendance, skips
        /// prejoin on open — see GetGuestJoinAsync); omit it for a generic guest link. Caller-
        /// side authorization is the controller's own SessionCalendarManagement:View gate, same
        /// as viewing the session itself.
        /// </summary>
        Task<GuestLinkDto> CreateGuestLinkAsync(Guid sessionId, Guid? childId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Mints a Guest Link token for a participant identified by name (and optionally email)
        /// rather than an enrolled Child — for DemoBookingService, whose lead may have no
        /// Child/BatchEnrollment row at all. GetGuestJoinAsync recognizes this shape of link
        /// (carries a "guestName" claim) and deliberately skips its own expiry/live-status gate
        /// for it, matching the Demo join redirect's long-standing "never expires, still works
        /// weeks later" contract this replaces — unlike <see cref="CreateGuestLinkAsync"/>'s
        /// links, which do expire at the session's end/Completion per the client's own
        /// requirement for that feature. Always joins as a non-moderator with prejoin skipped
        /// (the name is already known), same as a childId-bound Guest Link.
        /// </summary>
        Task<GuestLinkDto> CreateGuestLinkForParticipantAsync(Guid sessionId, string guestName, string? guestEmail, CancellationToken cancellationToken = default);

        /// <summary>
        /// Anonymous resolution of a Guest Link token into a live Jitsi join — called by the
        /// frontend's own unauthenticated "/guest-join" bridge page, not by a logged-in user.
        /// Re-checks the session's LIVE status/time on every call (not just the token's own
        /// baked-in expiry), so a link stops working the moment the class is marked Completed or
        /// Cancelled, or once its scheduled end has passed — see JwtTokenService.
        /// CreateGuestJoinToken's doc comment for why the token's own expiry is only a generous
        /// outer bound. Always mints a non-moderator Jitsi token, regardless of who originally
        /// generated the link. Throws DomainValidationException (not silently null) for an
        /// invalid/expired token or a class that isn't currently joinable — the landing page
        /// shows that message directly, there is no other UI around this call to fall back to.
        /// </summary>
        Task<GuestJoinDto> GetGuestJoinAsync(string token, CancellationToken cancellationToken = default);
    }
}
