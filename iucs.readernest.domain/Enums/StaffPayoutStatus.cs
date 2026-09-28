namespace iucs.readernest.domain.Enums
{
    /// <summary>
    /// A staff (non-teacher) monthly payout has no "Pending/accruing" state the way a teacher's
    /// Payout does — it's computed once, in full, either live (current month, not yet saved) or
    /// frozen (a past month, saved the first time it's viewed). Finalized is that frozen snapshot;
    /// Paid marks it disbursed, same meaning as Payout's own Paid status.
    /// </summary>
    public enum StaffPayoutStatus
    {
        Finalized,
        Paid,
    }
}
