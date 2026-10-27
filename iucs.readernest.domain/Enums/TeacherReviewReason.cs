namespace iucs.readernest.domain.Enums
{
    /// <summary>
    /// Why a class was sent to Payout Approvals, as stated by the teacher when ending it
    /// (mandatory whenever the class is submitted for admin review).
    /// </summary>
    public enum TeacherReviewReason
    {
        /// <summary>The parent cancelled the class.</summary>
        ParentCancelled,

        /// <summary>The teacher waited for the child for 10 minutes.</summary>
        WaitedForChild,

        /// <summary>The class was conducted for only part of its time (e.g. 10 or 20 minutes) -- the note carries the minutes and why.</summary>
        ShortDuration,

        /// <summary>Any other reason for the class status -- the note carries it.</summary>
        Other
    }
}
