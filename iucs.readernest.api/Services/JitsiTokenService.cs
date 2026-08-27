using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using iucs.readernest.application.Common.Interfaces;
using Microsoft.IdentityModel.Tokens;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Signs a Jitsi join token in the "iss/aud/sub/room + context.user" shape prosody's
    /// mod_auth_token (token_verification) expects. No-ops until an admin sets appId/appSecret
    /// on the "jitsi" Integration (Settings → Integrations → Jitsi Meet) — a deployment that
    /// hasn't turned on token verification simply ignores an unrecognised jwt option, so this
    /// stays backward-compatible with today's open self-hosted domain until that flag flips.
    /// </summary>
    public class JitsiTokenService : IJitsiTokenService
    {
        public string? CreateToken(
            string domain,
            string? jitsiConfigJson,
            string room,
            string participantName,
            string? participantEmail,
            bool moderator,
            DateTime expiresAtUtc)
        {
            var (appId, appSecret) = ReadCredentials(jitsiConfigJson);
            if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appSecret))
            {
                return null;
            }

            var now = DateTime.UtcNow;
            var payload = new JwtPayload(
                issuer: appId,
                audience: appId,
                claims: null,
                notBefore: now,
                expires: expiresAtUtc,
                issuedAt: now);
            payload["sub"] = domain;
            payload["room"] = room;
            payload["context"] = new Dictionary<string, object?>
            {
                ["user"] = new Dictionary<string, object?>
                {
                    ["name"] = participantName,
                    ["email"] = participantEmail,
                    ["moderator"] = moderator,
                },
            };

            var credentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(appSecret)),
                SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(new JwtHeader(credentials), payload);
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public bool ValidateFinalizeToken(string? bearerToken, string? jitsiConfigJson, string expectedRoom)
        {
            var (appId, appSecret) = ReadCredentials(jitsiConfigJson);
            if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(appSecret) || string.IsNullOrWhiteSpace(bearerToken))
            {
                return false;
            }

            var handler = new JwtSecurityTokenHandler();
            ClaimsPrincipal principal;
            try
            {
                principal = handler.ValidateToken(bearerToken, new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = appId,
                    ValidateAudience = true,
                    ValidAudience = appId,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(appSecret)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                }, out _);
            }
            catch (SecurityTokenException)
            {
                return false;
            }

            var purpose = principal.FindFirst("purpose")?.Value;
            var room = principal.FindFirst("room")?.Value;
            return purpose == "recording-finalize" && room == expectedRoom;
        }

        private static (string? AppId, string? AppSecret) ReadCredentials(string? configJson)
        {
            if (string.IsNullOrWhiteSpace(configJson))
            {
                return (null, null);
            }

            try
            {
                var config = JsonSerializer.Deserialize<Dictionary<string, string>>(configJson);
                if (config is null)
                {
                    return (null, null);
                }

                config.TryGetValue("appId", out var appId);
                config.TryGetValue("appSecret", out var appSecret);
                return (appId, appSecret);
            }
            catch (JsonException)
            {
                return (null, null);
            }
        }
    }
}
