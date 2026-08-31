using iucs.readernest.application.Dto.Activities;

namespace iucs.readernest.application.Services
{
    public interface IWhiteboardActivityService
    {
        Task<IReadOnlyList<WhiteboardActivityDto>> ListAsync(
            Guid? departmentId, Guid? courseId, CancellationToken cancellationToken = default);

        Task<WhiteboardActivityDto> CreateAsync(SaveWhiteboardActivityRequest request, CancellationToken cancellationToken = default);

        Task<WhiteboardActivityDto> UpdateAsync(Guid id, SaveWhiteboardActivityRequest request, CancellationToken cancellationToken = default);

        Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>Resolved activity set for one live class, same department/course scoping
        /// rule as <see cref="IQuizQuestionService.GetForSessionAsync"/> — this is what the
        /// classroom now launches from instead of a hardcoded fruit/letter/hotspot set.</summary>
        Task<IReadOnlyList<WhiteboardActivityDto>> GetForSessionAsync(
            Guid sessionId, Guid userId, CancellationToken cancellationToken = default);
    }
}
