namespace iucs.readernest.application.Dto.Users
{
    /// <summary>Delivery channel for an onboarding credential (re)send.</summary>
    public enum CredentialChannel
    {
        Email,
        WhatsApp
    }

    public class ResendCredentialsRequest
    {
        public CredentialChannel Channel { get; set; } = CredentialChannel.Email;
    }

    /// <summary>Which onboarding-credential delivery channels are currently enabled (Settings → Integrations is_enabled).</summary>
    public class CredentialChannelsDto
    {
        public bool Email { get; set; }

        public bool WhatsApp { get; set; }
    }
}
