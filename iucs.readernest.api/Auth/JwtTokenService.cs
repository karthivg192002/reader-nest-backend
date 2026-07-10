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
                new(ClaimTypes.Name, $"{user.FirstName} {user.LastName}"),
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
    }
}
