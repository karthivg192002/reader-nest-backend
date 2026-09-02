using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Dto.Auth;
using iucs.readernest.application.Helper;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace iucs.readernest.application.Services
{
    public class AuthService : IAuthService
    {
        // How long a self-service PIN reset link stays valid after it's emailed.
        private const int ResetTokenExpiryMinutes = 30;

        // Per-account lockout, on top of the login endpoint's per-IP rate limit (Program.cs) —
        // that one alone doesn't stop an attacker who knows one target's email and spreads
        // attempts across several source IPs from brute-forcing a 4-digit PIN's 10,000-value
        // keyspace against that one account.
        private const int MaxFailedLoginAttempts = 5;
        private const int LockoutMinutes = 15;

        private readonly IUnitOfWork _unitOfWork;
        private readonly IPasswordHasher _passwordHasher;
        private readonly ITokenService _tokenService;
        private readonly IAuditLogService _auditLog;
        private readonly INotificationService _notificationService;
        private readonly IConfiguration _configuration;
        private readonly IMenuService _menuService;

        public AuthService(
            IUnitOfWork unitOfWork,
            IPasswordHasher passwordHasher,
            ITokenService tokenService,
            IAuditLogService auditLog,
            INotificationService notificationService,
            IConfiguration configuration,
            IMenuService menuService)
        {
            _unitOfWork = unitOfWork;
            _passwordHasher = passwordHasher;
            _tokenService = tokenService;
            _auditLog = auditLog;
            _notificationService = notificationService;
            _configuration = configuration;
            _menuService = menuService;
        }

        public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var user = await _unitOfWork.Repository<User>()
                .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

            if (user is not null && user.LockoutEndUtc is { } lockoutEnd && lockoutEnd > DateTime.UtcNow)
            {
                var minutesLeft = Math.Max(1, (int)Math.Ceiling((lockoutEnd - DateTime.UtcNow).TotalMinutes));
                throw new UnauthorizedException($"Too many failed attempts. Try again in {minutesLeft} minute(s).");
            }

            if (user is null || !_passwordHasher.Verify(request.Pin, user.PinHash))
            {
                if (user is not null)
                {
                    user.FailedLoginAttempts++;
                    if (user.FailedLoginAttempts >= MaxFailedLoginAttempts)
                    {
                        user.LockoutEndUtc = DateTime.UtcNow.AddMinutes(LockoutMinutes);
                    }
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                throw new UnauthorizedException("Invalid email or PIN.");
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
            user.FailedLoginAttempts = 0;
            user.LockoutEndUtc = null;
            await _auditLog.StageAsync(AuditAction.Login, nameof(User), user.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var defaultRoute = await ResolveDefaultRouteAsync(user, cancellationToken);
            var accessiblePortals = await ResolveAccessiblePortalsAsync(user, permissions, cancellationToken);
            return BuildResponse(user, permissions, token, defaultRoute, accessiblePortals);
        }

        public async Task<LoginResponse> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var user = await _unitOfWork.Repository<User>().Query()
                .Include(u => u.TeacherProfile)
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken)
                ?? throw new NotFoundException(nameof(User), userId);

            var permissions = await LoadPermissionClaimsAsync(user, cancellationToken);
            var defaultRoute = await ResolveDefaultRouteAsync(user, cancellationToken);
            var accessiblePortals = await ResolveAccessiblePortalsAsync(user, permissions, cancellationToken);

            // Re-mints a fresh token carrying these just-recomputed permission claims — the
            // original login token's claims are frozen at issue time, so without this, a
            // Relationship Manager whose access an Admin just approved would keep hitting 403s
            // (and a stale sidebar) on every permission-gated call until they logged out and
            // back in. The frontend swaps its stored token for this one on every call here
            // (SessionProvider already polls this endpoint on load and periodically).
            var token = _tokenService.CreateToken(user, permissions);
            return BuildResponse(user, permissions, token, defaultRoute, accessiblePortals);
        }

        public async Task<CurrentAccessSnapshot?> GetCurrentAccessAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var user = await _unitOfWork.Repository<User>().Query()
                .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
            if (user is null)
            {
                return null;
            }

            return new CurrentAccessSnapshot
            {
                Role = user.Role,
                Status = user.Status,
                Permissions = await LoadPermissionClaimsAsync(user, cancellationToken),
            };
        }

        public async Task RequestPinResetAsync(ForgotPinRequest request, CancellationToken cancellationToken = default)
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var user = await _unitOfWork.Repository<User>()
                .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

            // Deliberately silent on "no such account" / inactive — an anonymous caller must
            // never be able to use this endpoint to discover which emails have accounts here.
            if (user is null || user.Status == UserStatus.Inactive)
            {
                return;
            }

            var token = SecureTokenGenerator.Generate();
            await _unitOfWork.Repository<PinResetToken>().AddAsync(
                new PinResetToken
                {
                    UserId = user.Id,
                    Token = token,
                    ExpiresAtUtc = DateTime.UtcNow.AddMinutes(ResetTokenExpiryMinutes),
                },
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var frontendBaseUrl = (_configuration["Frontend:BaseUrl"] ?? "http://localhost:5173").TrimEnd('/');
            var resetUrl = $"{frontendBaseUrl}/reset-pin?token={Uri.EscapeDataString(token)}";

            await _notificationService.SendTemplatedEmailAsync(
                user.Id,
                user.Email,
                NotificationType.General,
                "pin-reset",
                new Dictionary<string, string>
                {
                    ["FirstName"] = user.FirstName,
                    ["Email"] = user.Email,
                    ["ResetUrl"] = resetUrl,
                    ["ExpiryMinutes"] = ResetTokenExpiryMinutes.ToString(),
                },
                cancellationToken);
        }

        public async Task ResetPinAsync(ResetPinRequest request, CancellationToken cancellationToken = default)
        {
            var resetToken = await _unitOfWork.Repository<PinResetToken>()
                .FirstOrDefaultAsync(t => t.Token == request.Token, cancellationToken);

            if (resetToken is null || resetToken.UsedAtUtc is not null || resetToken.ExpiresAtUtc < DateTime.UtcNow)
            {
                throw new DomainValidationException("This reset link is invalid or has expired. Request a new one.");
            }

            var user = await _unitOfWork.Repository<User>().GetByIdAsync(resetToken.UserId, cancellationToken)
                ?? throw new NotFoundException(nameof(User), resetToken.UserId);

            user.PinHash = _passwordHasher.Hash(request.NewPin);
            resetToken.UsedAtUtc = DateTime.UtcNow;

            await _auditLog.StageAsync(AuditAction.Update, nameof(User), user.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
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

        /// <summary>
        /// Every portal this session may enter: the home portal MenuService.GetForUserAsync
        /// already resolves internally, plus any other portal holding a menu item this
        /// account's role has been explicitly granted View on via Menu Access — reuses that
        /// same method rather than re-deriving the rule, just projects the distinct portals
        /// out of whatever it returns.
        /// </summary>
        private async Task<IReadOnlyList<string>> ResolveAccessiblePortalsAsync(
            User user, IReadOnlyList<string> permissions, CancellationToken cancellationToken)
        {
            var viewableModules = permissions
                .Where(p => p.EndsWith($":{PermissionAction.View}", StringComparison.Ordinal))
                .Select(p => Enum.TryParse<PermissionModule>(p.Split(':')[0], out var m) ? (PermissionModule?)m : null)
                .Where(m => m.HasValue)
                .Select(m => m!.Value)
                .Distinct()
                .ToList();

            var menu = await _menuService.GetForUserAsync(user.Id, user.Role, viewableModules, cancellationToken);
            return menu.Select(m => m.Portal).Distinct().ToList();
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

        private static LoginResponse BuildResponse(
            User user, IReadOnlyList<string> permissions, TokenResult? token, string defaultRoute, IReadOnlyList<string> accessiblePortals)
        {
            return new LoginResponse
            {
                AccessToken = token?.AccessToken ?? string.Empty,
                ExpiresAtUtc = token?.ExpiresAtUtc ?? default,
                User = user.ToDto(),
                Permissions = permissions,
                DefaultRoute = defaultRoute,
                AccessiblePortals = accessiblePortals,
            };
        }
    }
}
