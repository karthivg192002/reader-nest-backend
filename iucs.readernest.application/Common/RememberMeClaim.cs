namespace iucs.readernest.application.Common
{
    /// <summary>
    /// Marks an access token issued for a "Remember me" login, so the /auth/me refresh re-issues
    /// another long-lived one instead of dropping back to the normal 8-hour lifetime.
    /// </summary>
    public static class RememberMeClaim
    {
        public const string Type = "rm";
    }
}
