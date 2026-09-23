namespace iucs.readernest.domain.Enums
{
    /// <summary>
    /// Admin / Management decision on a payout item flagged for review (a class that ran
    /// shorter than scheduled, or one with no teacher attendance recorded).
    /// </summary>
    public enum PayoutReviewDecision
    {
        ApprovedFull,
        ApprovedPartial,
        Rejected
    }
}
