namespace iucs.readernest.application.Common.Interfaces
{
    /// <summary>
    /// Transport for outbound SMS. The API-layer implementation reads its provider
    /// and credentials from the admin-configured "sms" integration and throws when
    /// it cannot deliver, so explicit admin actions can report success or failure.
    /// </summary>
    public interface ISmsSender
    {
        Task SendAsync(string toPhone, string message, CancellationToken cancellationToken = default);
    }
}
