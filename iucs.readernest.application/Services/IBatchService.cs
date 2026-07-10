using iucs.readernest.application.Dto.Batches;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Services
{
    public interface IBatchService
    {
        Task<IReadOnlyList<BatchDto>> ListAsync(BatchStatus? status, CancellationToken cancellationToken = default);

        Task<BatchDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

        Task<BatchDto> CreateAsync(SaveBatchRequest request, CancellationToken cancellationToken = default);

        Task<BatchDto> UpdateAsync(Guid id, SaveBatchRequest request, CancellationToken cancellationToken = default);

        Task<BatchDto> SetStatusAsync(Guid id, BatchStatus status, CancellationToken cancellationToken = default);
    }
}
