namespace iucs.readernest.domain.Enums
{
    /// <summary>
    /// How a batch's fee is expected to be collected. Exactly one applies at a time —
    /// AfterSessions and DueOnDate carry their own trigger value on the Batch
    /// (PaymentAfterSessionsCount / PaymentDueDate) and the other is left null.
    /// </summary>
    public enum BatchPaymentPlanType
    {
        FullPaymentDone,
        AfterSessions,
        DueOnDate
    }
}
