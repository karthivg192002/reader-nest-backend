using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Notes;
using iucs.readernest.domain.Entities.Notes;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class FloatingNoteService : IFloatingNoteService
    {
        private readonly IUnitOfWork _unitOfWork;

        public FloatingNoteService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<IReadOnlyList<FloatingNoteDto>> ListMineAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var notes = await _unitOfWork.Repository<FloatingNote>().Query()
                .Where(n => n.UserId == userId)
                .OrderBy(n => n.SortOrder).ThenBy(n => n.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            return notes.Select(ToDto).ToList();
        }

        public async Task<FloatingNoteDto> CreateAsync(
            Guid userId,
            SaveFloatingNoteRequest request,
            CancellationToken cancellationToken = default)
        {
            var note = new FloatingNote { UserId = userId };
            Apply(note, request);

            await _unitOfWork.Repository<FloatingNote>().AddAsync(note, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(note);
        }

        public async Task<FloatingNoteDto> UpdateAsync(
            Guid userId,
            Guid id,
            SaveFloatingNoteRequest request,
            CancellationToken cancellationToken = default)
        {
            var repository = _unitOfWork.Repository<FloatingNote>();
            var note = await repository.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken)
                ?? throw new NotFoundException(nameof(FloatingNote), id);

            Apply(note, request);
            repository.Update(note);

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(note);
        }

        public async Task DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken = default)
        {
            var repository = _unitOfWork.Repository<FloatingNote>();
            var note = await repository.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId, cancellationToken)
                ?? throw new NotFoundException(nameof(FloatingNote), id);

            repository.Remove(note);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        private static void Apply(FloatingNote note, SaveFloatingNoteRequest request)
        {
            note.Content = request.Content ?? string.Empty;
            note.Color = string.IsNullOrWhiteSpace(request.Color) ? null : request.Color.Trim();
            note.SortOrder = request.SortOrder;
        }

        private static FloatingNoteDto ToDto(FloatingNote note) => new()
        {
            Id = note.Id,
            Content = note.Content,
            Color = note.Color,
            SortOrder = note.SortOrder,
            CreatedAtUtc = note.CreatedAtUtc,
            UpdatedAtUtc = note.UpdatedAtUtc,
        };
    }
}
