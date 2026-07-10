namespace iucs.readernest.application.Common.Interfaces
{
    /// <summary>
    /// Transport for outbound email. Sprint 1 ships a logging stub in the API layer;
    /// a real SMTP/provider implementation replaces it when credentials are available.
    /// </summary>
    public interface IEmailSender
    {
        Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default);
    }
}
