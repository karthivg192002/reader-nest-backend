using iucs.readernest.application.Dto.Users;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Services
{
    /// <summary>
    /// A Sub Admin's self-service "request additional access" flow: submit, list your own
    /// history, or (Admin) list and review everyone's requests.
    /// </summary>
    public interface IAccessRequestService
    {
        Task<AccessRequestDto> SubmitAsync(
            Guid requestedByUserId,
            SubmitAccessRequestRequest request,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<AccessRequestDto>> ListMineAsync(
            Guid requestedByUserId,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<AccessRequestDto>> ListAsync(
            AccessRequestStatus? status,
            CancellationToken cancellationToken = default);

        Task<AccessRequestDto> ReviewAsync(
            Guid id,
            Guid reviewerUserId,
            ReviewAccessRequestRequest request,
            CancellationToken cancellationToken = default);
    }
}
