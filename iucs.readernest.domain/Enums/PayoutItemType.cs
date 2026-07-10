namespace iucs.readernest.domain.Enums
{
    /// <summary>
    /// Line-item categories on a monthly payout. Student no-show adds a waiting amount;
    /// teacher no-show and penalties are negative deductions.
    /// </summary>
    public enum PayoutItemType
    {
        SessionEarning,
        StudentNoShowWaiting,
        TeacherNoShowDeduction,
        Penalty,
        Bonus,
        Adjustment
    }
}
