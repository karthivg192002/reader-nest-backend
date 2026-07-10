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

        Task<SessionRecordingDto> AddRecordingAsync(
            Guid sessionId,
            RegisterRecordingRequest request,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<SessionRecordingDto>> ListRecordingsAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default);
    }
}
