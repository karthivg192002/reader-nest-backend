namespace iucs.readernest.domain.Enums
{
    /// <summary>
    /// Admission funnel stages for a demo booking.
    /// </summary>
    public enum ConversionStatus
    {
        DemoScheduled,
        DemoCompleted,
        FollowUpInProgress,
        PaymentPending,
        PartiallyPaid,
        Enrolled,
        NotInterested
    }
}
