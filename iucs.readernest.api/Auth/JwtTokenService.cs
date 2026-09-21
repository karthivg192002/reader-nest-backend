using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.domain.Entities.Users;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace iucs.readernest.api.Auth
{
    public class JwtTokenService : ITokenService
    {
        /// <summary>Claim type carrying a Sub Admin's "Module:Action" grants.</summary>
        public const string PermissionClaimType = "perm";

        private readonly JwtOptions _options;

        public JwtTokenService(IOptions<JwtOptions> options)
        {
            _options = options.Value;
        }

        public TokenResult CreateToken(User user, IReadOnlyCollection<string> permissionClaims)
        {
            var expiresAtUtc = DateTime.UtcNow.AddMinutes(_options.AccessTokenMinutes);

            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new(JwtRegisteredClaimNames.Email, user.Email),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Name, $"{user.FirstName} {user.LastName}".Trim()),
                new(ClaimTypes.Role, user.Role.ToString()),
            };
            claims.AddRange(permissionClaims.Select(p => new Claim(PermissionClaimType, p)));

            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
                SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _options.Issuer,
                audience: _options.Audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: expiresAtUtc,
                signingCredentials: credentials);

            return new TokenResult
            {
                AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
                ExpiresAtUtc = expiresAtUtc,
            };
        }

        public TokenResult CreateRecordingObserverHubToken(Guid sessionId, DateTime expiresAtUtc)
        {
            // A throwaway subject — nothing ever looks this id up as a real user, unlike the
            // NameIdentifier CreateToken issues above, so it deliberately isn't a real user id.
            var syntheticUserId = Guid.NewGuid();
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, syntheticUserId.ToString()),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(ClaimTypes.NameIdentifier, syntheticUserId.ToString()),
                new(ClaimTypes.Name, "Recording"),
                new("purpose", "recording-observer"),
                new("sessionId", sessionId.ToString()),
            };

            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
                SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _options.Issuer,
                audience: _options.Audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: expiresAtUtc,
                signingCredentials: credentials);

            return new TokenResult
            {
                AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
                ExpiresAtUtc = expiresAtUtc,
            };
        }

        private const string GuestJoinPurpose = "guest-join";
        private const string GuestClassroomPurpose = "guest-classroom";
        private const string SessionIdClaimType = "sessionId";
        private const string ChildIdClaimType = "childId";
        private const string GuestNameClaimType = "guestName";
        private const string GuestEmailClaimType = "guestEmail";

        public TokenResult CreateGuestClassroomHubToken(Guid sessionId, Guid? childId, string participantName, DateTime expiresAtUtc)
        {
            // A throwaway subject, same convention as CreateRecordingObserverHubToken just above
            // — nothing ever looks this id up as a real user.
            var syntheticUserId = Guid.NewGuid();
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Sub, syntheticUserId.ToString()),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new(ClaimTypes.NameIdentifier, syntheticUserId.ToString()),
                new(ClaimTypes.Name, participantName),
                new("purpose", GuestClassroomPurpose),
                new(SessionIdClaimType, sessionId.ToString()),
            };
            if (childId is Guid cid)
            {
                claims.Add(new Claim(ChildIdClaimType, cid.ToString()));
            }

            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
                SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _options.Issuer,
                audience: _options.Audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: expiresAtUtc,
                signingCredentials: credentials);

            return new TokenResult
            {
                AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
                ExpiresAtUtc = expiresAtUtc,
            };
        }

        public TokenResult CreateGuestJoinToken(Guid sessionId, Guid? childId, DateTime expiresAtUtc, string? guestName = null, string? guestEmail = null)
        {
            var claims = new List<Claim>
            {
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new("purpose", GuestJoinPurpose),
                new(SessionIdClaimType, sessionId.ToString()),
            };
            if (childId is Guid cid)
            {
                claims.Add(new Claim(ChildIdClaimType, cid.ToString()));
            }
            if (!string.IsNullOrWhiteSpace(guestName))
            {
                claims.Add(new Claim(GuestNameClaimType, guestName));
            }
            if (!string.IsNullOrWhiteSpace(guestEmail))
            {
                claims.Add(new Claim(GuestEmailClaimType, guestEmail));
            }

            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
                SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: _options.Issuer,
                audience: _options.Audience,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: expiresAtUtc,
                signingCredentials: credentials);

            return new TokenResult
            {
                AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
                ExpiresAtUtc = expiresAtUtc,
            };
        }

        public (Guid SessionId, Guid? ChildId, string? GuestName, string? GuestEmail)? ValidateGuestJoinToken(string token)
        {
            // This backs a public, [AllowAnonymous] endpoint (SessionsController.GuestJoin) —
            // unlike every other caller of ValidateToken in this file, the input here can be
            // anything anyone sends, not just a token this app itself minted. A null/empty
            // string trips ValidateToken's own ArgumentNullException/ArgumentException before it
            // ever gets to a SecurityTokenException, which would otherwise surface as an
            // unhandled 500 instead of the friendly "this link is invalid" 400 every other bad
            // token already gets below.
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }

            var handler = new JwtSecurityTokenHandler();
            ClaimsPrincipal principal;
            try
            {
                principal = handler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = _options.Issuer,
                    ValidateAudience = true,
                    ValidAudience = _options.Audience,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                }, out _);
            }
            catch (SecurityTokenException)
            {
                return null;
            }

            if (principal.FindFirst("purpose")?.Value != GuestJoinPurpose)
            {
                return null;
            }

            if (!Guid.TryParse(principal.FindFirst(SessionIdClaimType)?.Value, out var sessionId))
            {
                return null;
            }

            Guid? childId = Guid.TryParse(principal.FindFirst(ChildIdClaimType)?.Value, out var cid) ? cid : null;
            var guestName = principal.FindFirst(GuestNameClaimType)?.Value;
            var guestEmail = principal.FindFirst(GuestEmailClaimType)?.Value;
            return (sessionId, childId, guestName, guestEmail);
        }
    }
}
