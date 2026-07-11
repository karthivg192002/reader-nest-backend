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
    }
}
