using iucs.readernest.application.Dto.Auth;

namespace iucs.readernest.application.Services
{
    public interface IAuthService
    {
        Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

        Task<LoginResponse> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default);
    }
}
