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

        /// <summary>The batch's current student roster (WBS p.17 "Assign Students").</summary>
        Task<IReadOnlyList<BatchStudentDto>> ListEnrollmentsAsync(Guid batchId, CancellationToken cancellationToken = default);

        /// <summary>Active, approved children not yet placed in this batch — candidates for the assign picker.</summary>
        Task<IReadOnlyList<UnassignedChildDto>> ListUnassignedStudentsAsync(Guid batchId, CancellationToken cancellationToken = default);

        /// <summary>Places a child in the batch; rejects when the batch is already at capacity.</summary>
        Task<BatchStudentDto> AssignStudentAsync(Guid batchId, Guid childId, CancellationToken cancellationToken = default);

        /// <summary>Withdraws a child from the batch, freeing a seat.</summary>
        Task RemoveStudentAsync(Guid batchId, Guid childId, CancellationToken cancellationToken = default);
    }
}
