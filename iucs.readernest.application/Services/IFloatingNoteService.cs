using iucs.readernest.application.Dto.Notes;

namespace iucs.readernest.application.Services
{
    /// <summary>The signed-in user's own floating notes widget content.</summary>
    public interface IFloatingNoteService
    {
        Task<IReadOnlyList<FloatingNoteDto>> ListMineAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<FloatingNoteDto> CreateAsync(Guid userId, SaveFloatingNoteRequest request, CancellationToken cancellationToken = default);

        Task<FloatingNoteDto> UpdateAsync(Guid userId, Guid id, SaveFloatingNoteRequest request, CancellationToken cancellationToken = default);

        Task DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);
    }
}
