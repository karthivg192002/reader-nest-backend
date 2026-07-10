namespace iucs.readernest.domain.Enums
{
    /// <summary>
    /// Active batches are currently running; Dormant batches have finished
    /// (moved automatically on course completion) and drive the 15-day recording window.
    /// </summary>
    public enum BatchStatus
    {
        Active,
        Dormant,
        Archived
    }
}
