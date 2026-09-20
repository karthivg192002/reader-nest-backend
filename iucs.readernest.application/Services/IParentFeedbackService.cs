using iucs.readernest.application.Dto.Admission;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Services
{
    /// <summary>Parent star-rating feedback after a demo class and after a completed course.</summary>
    public interface IParentFeedbackService
    {
        /// <summary>Recent demos / completed courses this parent hasn't rated yet, newest first.</summary>
        Task<IReadOnlyList<PendingParentFeedbackDto>> GetPendingAsync(Guid parentUserId, CancellationToken cancellationToken = default);

        /// <summary>Saves the rating (one per demo, one per child+batch) and alerts Admin + Admission.</summary>
        Task SubmitAsync(Guid parentUserId, SubmitParentFeedbackRequest request, CancellationToken cancellationToken = default);

        /// <summary>Everything parents have submitted, newest first, optionally one kind only.</summary>
        Task<IReadOnlyList<ParentFeedbackDto>> ListAsync(ParentFeedbackKind? kind, CancellationToken cancellationToken = default);
    }
}
