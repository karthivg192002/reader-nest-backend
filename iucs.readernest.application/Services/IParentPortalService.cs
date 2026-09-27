using iucs.readernest.application.Dto.Billing;
using iucs.readernest.application.Dto.Portal;
using iucs.readernest.application.Dto.Resources;
using iucs.readernest.application.Dto.Sessions;

namespace iucs.readernest.application.Services
{
    public interface IParentPortalService
    {
        /// <summary>Unified multi-child dashboard: classes done/remaining, attendance %, fee status, suspension flag.</summary>
        Task<ParentDashboardDto> GetDashboardAsync(Guid parentUserId, CancellationToken cancellationToken = default);

        /// <summary>Sessions of every batch the parent's children are enrolled in.</summary>
        Task<IReadOnlyList<ClassSessionDto>> GetScheduleAsync(
            Guid parentUserId,
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken cancellationToken = default);

        /// <summary>Resources granted to this parent; blocked while fee-suspended.</summary>
        Task<IReadOnlyList<ResourceDto>> GetResourcesAsync(Guid parentUserId, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(Guid parentUserId, CancellationToken cancellationToken = default);

        /// <summary>Whether this parent still has to accept the Terms and Conditions (only their first payment asks).</summary>
        Task<bool> IsTermsAcceptanceRequiredAsync(Guid parentUserId, CancellationToken cancellationToken = default);

        /// <summary>Records the parent's Terms and Conditions acceptance (idempotent: the first time wins).</summary>
        Task AcceptTermsAsync(Guid parentUserId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Validates the grant, downloadability flag and suspension state before
        /// handing back the resource for a parent download.
        /// </summary>
        Task<ResourceDto> GetResourceForDownloadAsync(Guid parentUserId, Guid resourceId, CancellationToken cancellationToken = default);

        /// <summary>Same access checks as a download, for viewing in the portal (no downloadable flag needed).</summary>
        Task<ResourceDto> GetResourceForViewAsync(Guid parentUserId, Guid resourceId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Non-expired recordings for a session, once the caller's own child is confirmed
        /// enrolled in that session's batch. The "15-day parent view window" this feature
        /// is documented around (SessionService.AddRecordingAsync, Batch.CompletedAtUtc)
        /// had no parent-reachable endpoint anywhere until this one.
        /// </summary>
        Task<IReadOnlyList<SessionRecordingDto>> GetRecordingsAsync(
            Guid parentUserId, Guid sessionId, CancellationToken cancellationToken = default);

        /// <summary>Every non-expired recording across every one of the caller's children's
        /// batches, in one query -- see ParentRecordingDto's own doc comment for the N+1
        /// pattern this replaces.</summary>
        Task<IReadOnlyList<ParentRecordingDto>> GetMyRecordingsAsync(
            Guid parentUserId, CancellationToken cancellationToken = default);
    }
}
