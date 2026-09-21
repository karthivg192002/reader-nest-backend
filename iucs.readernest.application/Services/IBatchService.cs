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

        /// <summary>
        /// Soft-deletes the batch (excluded from every future query via the global IsDeleted
        /// filter). Refused while it still has an active student — unlike Archive, a deleted
        /// batch disappears from every list rather than just changing status, which would strand
        /// that student with nothing to show for them; withdraw them (or move them to another
        /// batch) first.
        /// </summary>
        Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>
        /// One-time data repair: moves every still-undelivered <c>ClassSession</c> that's fallen
        /// out of sync with its own batch's current teacher (left over from a reassignment that
        /// happened before <see cref="UpdateAsync"/>'s cascade fix existed) onto the batch's
        /// actual current teacher. Safe to run repeatedly — nothing left to fix is a no-op.
        /// </summary>
        Task<ReconcileStaleSessionTeachersResultDto> ReconcileStaleSessionTeachersAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// One-time data repair: corrects the <c>ScheduledEndAtUtc</c> of every still-undelivered
        /// <c>ClassSession</c> whose real length no longer matches its own batch's current
        /// effective duration (left over from a duration edit that happened before
        /// <see cref="UpdateAsync"/>'s cascade fix existed). Safe to run repeatedly — nothing
        /// left to fix is a no-op.
        /// </summary>
        Task<ReconcileStaleSessionDurationsResultDto> ReconcileStaleSessionDurationsAsync(CancellationToken cancellationToken = default);

        /// <summary>The batch's current student roster (WBS p.17 "Assign Students").</summary>
        Task<IReadOnlyList<BatchStudentDto>> ListEnrollmentsAsync(Guid batchId, CancellationToken cancellationToken = default);

        /// <summary>Active, approved children not yet placed in this batch — candidates for the assign picker.</summary>
        Task<IReadOnlyList<UnassignedChildDto>> ListUnassignedStudentsAsync(Guid batchId, CancellationToken cancellationToken = default);

        /// <summary>A teacher's own "My Students" roster across every batch they're assigned to
        /// — see TeacherStudentDto's own doc comment for the feedback this answers.</summary>
        Task<IReadOnlyList<TeacherStudentDto>> ListMyStudentsAsync(Guid teacherUserId, CancellationToken cancellationToken = default);

        /// <summary>Places a child in the batch; rejects when the batch is already at capacity.</summary>
        Task<BatchStudentDto> AssignStudentAsync(Guid batchId, Guid childId, CancellationToken cancellationToken = default);

        /// <summary>Withdraws a child from the batch, freeing a seat.</summary>
        Task RemoveStudentAsync(Guid batchId, Guid childId, CancellationToken cancellationToken = default);
    }
}
