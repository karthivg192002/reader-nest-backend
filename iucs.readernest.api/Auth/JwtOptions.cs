namespace iucs.readernest.api.Auth
{
    public class JwtOptions
    {
        public const string SectionName = "Jwt";

        public string Issuer { get; set; } = null!;

        public string Audience { get; set; } = null!;

        /// <summary>Symmetric signing key; supply via user-secrets or environment in real deployments.</summary>
        public string SigningKey { get; set; } = null!;

        public int AccessTokenMinutes { get; set; } = 480;
    }
}
