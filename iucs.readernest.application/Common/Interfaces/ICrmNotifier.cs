namespace iucs.readernest.application.Common.Interfaces
{
    /// <summary>
    /// CRM integration hook: lead events (demo bookings and conversion changes)
    /// are pushed to the client's CRM as JSON webhooks. A missing webhook URL
    /// makes every call a no-op, so the platform runs fine without a CRM.
    /// </summary>
    public interface ICrmNotifier
    {
        Task PushLeadEventAsync(string eventType, object payload, CancellationToken cancellationToken = default);
    }
}
