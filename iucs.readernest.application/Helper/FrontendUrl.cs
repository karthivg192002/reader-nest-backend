namespace iucs.readernest.application.Helper
{
    public static class FrontendUrl
    {
        /// <summary>
        /// The front-end address to build a shareable link from. The configured
        /// <c>Frontend:BaseUrl</c> is one fixed value per deployment, but the UAT API is called from
        /// the UAT site while still configured with the production address -- so an invite link
        /// copied on UAT opened production. When the browser's own <c>Origin</c> is one of the
        /// origins this API already trusts for CORS, that is the right site to link back to; any
        /// other value (missing, spoofed, unlisted) falls back to the configured address, so a
        /// caller can never steer a link to a host that isn't on the allow-list.
        /// </summary>
        public static string Resolve(string? configuredBaseUrl, string? requestOrigin, IEnumerable<string>? allowedOrigins)
        {
            var configured = (string.IsNullOrWhiteSpace(configuredBaseUrl) ? "http://localhost:5173" : configuredBaseUrl).Trim().TrimEnd('/');

            var origin = requestOrigin?.Trim().TrimEnd('/');
            if (!string.IsNullOrEmpty(origin)
                && allowedOrigins is not null
                && allowedOrigins.Any(a => string.Equals(a?.Trim().TrimEnd('/'), origin, StringComparison.OrdinalIgnoreCase)))
            {
                return origin;
            }

            return configured;
        }
    }
}
