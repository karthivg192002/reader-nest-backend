using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Dto.Auth;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class AuthService : IAuthService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IPasswordHasher _passwordHasher;
        private readonly ITokenService _tokenService;
        private readonly IAuditLogService _auditLog;

        public AuthService(
            IUnitOfWork unitOfWork,
            IPasswordHasher passwordHasher,
            ITokenService tokenService,
            IAuditLogService auditLog)
        {
            _unitOfWork = unitOfWork;
            _passwordHasher = passwordHasher;
            _tokenService = tokenService;
            _auditLog = auditLog;
        }

        public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var user = await _unitOfWork.Repository<User>()
                .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

            if (user is null || !_passwordHasher.Verify(request.Password, user.PasswordHash))
            {
                throw new UnauthorizedException("Invalid email or password.");
            }

            if (user.Status == UserStatus.Inactive)
            {
                throw new UnauthorizedException("This account has been deactivated. Contact the administrator.");
            }

            // Suspended users (fee default) may still log in: the portal shows the
            // pending-fee "Pay Now" popup and restricts content, per the requirement.
            var permissions = await LoadPermissionClaimsAsync(user, cancellationToken);
            var token = _tokenService.CreateToken(user, permissions);

            user.LastLoginAtUtc = DateTime.UtcNow;
            await _auditLog.StageAsync(AuditAction.Login, nameof(User), user.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return BuildResponse(user, permissions, token);
        }

        public async Task<LoginResponse> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var user = await _unitOfWork.Repository<User>().Query()
                .Include(u => u.TeacherProfile)
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                ?? throw new NotFoundException(nameof(User), userId);

            var permissions = await LoadPermissionClaimsAsync(user, cancellationToken);

            // No new token on refresh-of-self; caller keeps using its current one.
            return BuildResponse(user, permissions, null);
        }

        private async Task<IReadOnlyList<string>> LoadPermissionClaimsAsync(User user, CancellationToken cancellationToken)
        {
            if (user.Role != UserRole.SubAdmin)
            {
                return [];
            }

            var grants = await _unitOfWork.Repository<SubAdminPermission>().Query()
                .Where(p => p.UserId == user.Id)
                .ToListAsync(cancellationToken);

            var claims = new List<string>();
            foreach (var grant in grants)
            {
                if (grant.CanView) claims.Add($"{grant.Module}:{PermissionAction.View}");
                if (grant.CanCreate) claims.Add($"{grant.Module}:{PermissionAction.Create}");
                if (grant.CanEdit) claims.Add($"{grant.Module}:{PermissionAction.Edit}");
                if (grant.CanDelete) claims.Add($"{grant.Module}:{PermissionAction.Delete}");
                if (grant.CanApprove) claims.Add($"{grant.Module}:{PermissionAction.Approve}");
            }

            return claims;
        }

        private static LoginResponse BuildResponse(User user, IReadOnlyList<string> permissions, TokenResult? token)
        {
            return new LoginResponse
            {
                AccessToken = token?.AccessToken ?? string.Empty,
                ExpiresAtUtc = token?.ExpiresAtUtc ?? default,
                User = user.ToDto(),
                Permissions = permissions,
            };
        }
    }
}
