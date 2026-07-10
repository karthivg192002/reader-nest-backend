namespace iucs.readernest.domain.Enums
{
    /// <summary>
    /// Drives the colour-coded academic calendar. A no-show by either party
    /// results in a new session created with a CarriedForward link to this one.
    /// </summary>
    public enum SessionStatus
    {
        Scheduled,
        InProgress,
        Completed,
        Cancelled,
        Rescheduled,
        TeacherNoShow,
        StudentNoShow,
        CarriedForward
    }
}
