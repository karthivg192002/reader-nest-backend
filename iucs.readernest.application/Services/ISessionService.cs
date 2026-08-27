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
        /// Marks a session completed and, when all course sessions of the batch are done,
        /// automatically moves the batch to Dormant (course completion tracking).
        /// </summary>
        Task<ClassSessionDto> CompleteAsync(
            Guid id,
            CompleteSessionRequest? request = null,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ClassSessionDto>> GenerateScheduleAsync(
            Guid batchId,
            GenerateScheduleRequest request,
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

        Task<SessionRecordingDto> AddRecordingAsync(
            Guid sessionId,
            RegisterRecordingRequest request,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<SessionRecordingDto>> ListRecordingsAsync(
            Guid sessionId,
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
        /// The room + (when configured) a signed, room-scoped join token for the caller.
        /// Authorized the same way as the ClassroomHub: Admin, the assigned teacher, or a
        /// parent with a child enrolled in the session's batch — anyone else is refused
        /// before a token is ever minted.
        /// </summary>
        Task<JitsiJoinDto> GetJitsiJoinAsync(Guid sessionId, Guid userId, CancellationToken cancellationToken = default);

        /// <summary>Non-secret Jitsi settings (domain, auto-record) for whoever is about to join a live class.</summary>
        Task<ClassroomSettingsDto> GetClassroomSettingsAsync(CancellationToken cancellationToken = default);
    }
}
