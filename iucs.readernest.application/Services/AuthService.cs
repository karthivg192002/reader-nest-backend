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

            var defaultRoute = await ResolveDefaultRouteAsync(user, cancellationToken);
            return BuildResponse(user, permissions, token, defaultRoute);
        }

        public async Task<LoginResponse> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var user = await _unitOfWork.Repository<User>().Query()
                .Include(u => u.TeacherProfile)
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                ?? throw new NotFoundException(nameof(User), userId);

            var permissions = await LoadPermissionClaimsAsync(user, cancellationToken);
            var defaultRoute = await ResolveDefaultRouteAsync(user, cancellationToken);

            // No new token on refresh-of-self; caller keeps using its current one.
            return BuildResponse(user, permissions, null, defaultRoute);
        }

        /// <summary>
        /// The frontend route a user lands on right after login: their assigned
        /// role's configured route for Sub Admins, else the fixed portal home
        /// for their account type.
        /// </summary>
        private async Task<string> ResolveDefaultRouteAsync(User user, CancellationToken cancellationToken)
        {
            if (user.Role == UserRole.SubAdmin && user.RoleDefinitionId.HasValue)
            {
                var role = await _unitOfWork.Repository<RoleDefinition>()
                    .GetByIdAsync(user.RoleDefinitionId.Value, cancellationToken);
                if (!string.IsNullOrWhiteSpace(role?.DefaultRoute))
                {
                    return role.DefaultRoute;
                }
            }

            return user.Role switch
            {
                UserRole.Admin => "/admin",
                UserRole.Teacher => "/teacher",
                UserRole.Parent => "/parent",
                UserRole.AdmissionTeam => "/admission",
                UserRole.SubAdmin => "/subadmin",
                _ => "/login",
            };
        }

        /// <summary>System RoleDefinition name backing each fixed-portal UserRole's default grant.</summary>
        private static string? SystemRoleNameFor(UserRole role) => role switch
        {
            UserRole.Teacher => "teacher",
            UserRole.Parent => "parent",
            UserRole.AdmissionTeam => "admission",
            _ => null,
        };

        /// <summary>
        /// Sub Admins carry their own per-user grant (SubAdminPermission — possibly seeded
        /// from a preset but editable per person from then on). Every other non-Admin role
        /// (Teacher/Parent/Admission Team) shares one grant: its matching system
        /// RoleDefinition, editable from the same Roles &amp; Permissions → Role Presets screen
        /// as every other role — there is no separate hardcoded baseline to keep in sync.
        /// Admin needs no claims; the authorization handler grants it every permission by role.
        /// </summary>
        private async Task<IReadOnlyList<string>> LoadPermissionClaimsAsync(User user, CancellationToken cancellationToken)
        {
            if (user.Role == UserRole.SubAdmin)
            {
                var grants = await _unitOfWork.Repository<SubAdminPermission>().Query()
                    .Where(p => p.UserId == user.Id)
                    .ToListAsync(cancellationToken);
                return ToClaims(grants.Select(g => (g.Module, g.CanView, g.CanCreate, g.CanEdit, g.CanDelete, g.CanApprove)));
            }

            var roleName = SystemRoleNameFor(user.Role);
            if (roleName is null)
            {
                return [];
            }

            var role = await _unitOfWork.Repository<RoleDefinition>().Query()
                .Include(r => r.Permissions)
                .FirstOrDefaultAsync(r => r.Name == roleName, cancellationToken);

            return role is null
                ? []
                : ToClaims(role.Permissions.Select(p => (p.Module, p.CanView, p.CanCreate, p.CanEdit, p.CanDelete, p.CanApprove)));
        }

        private static List<string> ToClaims(
            IEnumerable<(PermissionModule Module, bool CanView, bool CanCreate, bool CanEdit, bool CanDelete, bool CanApprove)> grants)
        {
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

        private static LoginResponse BuildResponse(User user, IReadOnlyList<string> permissions, TokenResult? token, string defaultRoute)
        {
            return new LoginResponse
            {
                AccessToken = token?.AccessToken ?? string.Empty,
                ExpiresAtUtc = token?.ExpiresAtUtc ?? default,
                User = user.ToDto(),
                Permissions = permissions,
                DefaultRoute = defaultRoute,
            };
        }
    }
}
