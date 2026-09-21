namespace iucs.readernest.application.Dto.Users
{
    /// <summary>Delivery channel for an onboarding credential (re)send.</summary>
    public enum CredentialChannel
    {
        Email,
        WhatsApp,
        Sms
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

        public bool Sms { get; set; }
    }

    /// <summary>
    /// The freshly-generated PIN, handed back so an admin can read it out or write it down —
    /// unlike ResendCredentialsAsync, nothing is emailed/texted, so this doesn't depend on a
    /// delivery channel being configured and works even when the account has no phone/email
    /// the parent can currently reach.
    /// </summary>
    /// <summary>The system-issued PIN an admin asked to view again (see IPinVault).</summary>
    public class RevealedPinDto
    {
        public string Pin { get; set; } = null!;
    }

    public class ResetPinResultDto
    {
        public string TemporaryPin { get; set; } = null!;
    }
}
