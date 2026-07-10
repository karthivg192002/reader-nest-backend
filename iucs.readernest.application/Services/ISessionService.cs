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
        Task<ClassSessionDto> CompleteAsync(Guid id, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ClassSessionDto>> GenerateScheduleAsync(
            Guid batchId,
            GenerateScheduleRequest request,
            CancellationToken cancellationToken = default);
    }
}
