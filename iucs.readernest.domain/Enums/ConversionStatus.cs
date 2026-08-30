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
        NotInterested,
        /// <summary>
        /// Staff has decided this lead is converting. Entering this status (from any other,
        /// via UpdateConversionStatusAsync) auto-creates the parent's login — welcome-credentials
        /// email with a temporary PIN, same as an admin adding them through Users — if one
        /// doesn't already exist for their email (a sibling's earlier demo, say, reuses the
        /// existing account instead of creating a duplicate or re-emailing credentials). The
        /// child record itself still comes from the parent's own mandatory first-login
        /// enrollment form once approved, same as every other enrollment path.
        /// </summary>
        ReadyForEnrollment,
    }
}
