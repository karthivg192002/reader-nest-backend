using System.Security.Claims;
using iucs.readernest.domain.Common;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Claims-based implementation of <see cref="ICurrentUserService"/>. Returns null
    /// until authentication ships (Sprint 1), which the audit interceptor records
    /// as a system action.
    /// </summary>
    public class CurrentUserService : ICurrentUserService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CurrentUserService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        public Guid? UserId
        {
            get
            {
                var idClaim = _httpContextAccessor.HttpContext?.User
                    .FindFirstValue(ClaimTypes.NameIdentifier);

                return Guid.TryParse(idClaim, out var userId) ? userId : null;
            }
        }
    }
}
