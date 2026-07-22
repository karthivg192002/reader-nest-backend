using System.Text.Json;

namespace iucs.readernest.application.Helper
{
    /// <summary>Builds the direct, no-login Jitsi room URL used in booking/reminder emails.</summary>
    public static class JitsiLinkBuilder
    {
        private const string DefaultDomain = "meet.techmisai.com";

        /// <summary>
        /// <paramref name="integrationConfigJson"/> is the "jitsi" Integration's ConfigJson
        /// (expects a "domain" key); falls back to the seeded default domain if missing/unparseable.
        /// Returns null when there's no meeting room to link to.
        /// </summary>
        public static string? BuildJoinUrl(string? meetingRoomId, string? integrationConfigJson)
        {
            if (string.IsNullOrWhiteSpace(meetingRoomId))
            {
                return null;
            }

            var domain = DefaultDomain;
            if (!string.IsNullOrWhiteSpace(integrationConfigJson))
            {
                try
                {
                    var config = JsonSerializer.Deserialize<Dictionary<string, string>>(integrationConfigJson);
                    if (config is not null && config.TryGetValue("domain", out var configuredDomain)
                        && !string.IsNullOrWhiteSpace(configuredDomain))
                    {
                        domain = configuredDomain;
                    }
                }
                catch (JsonException)
                {
                    // Malformed config — fall back to the default domain.
                }
            }

            return $"https://{domain}/{meetingRoomId}";
        }
    }
}
