namespace iucs.readernest.application.Common.Interfaces
{
    /// <summary>
    /// Transport for outbound WhatsApp messages. The API-layer implementation reads
    /// its credentials from the admin-configured "whatsapp" integration and throws
    /// when it cannot deliver, so explicit admin actions (e.g. resending onboarding
    /// credentials) can report success or failure rather than failing silently.
    /// </summary>
    public interface IWhatsAppSender
    {
        Task SendAsync(string toPhone, string message, CancellationToken cancellationToken = default);
    }
}
