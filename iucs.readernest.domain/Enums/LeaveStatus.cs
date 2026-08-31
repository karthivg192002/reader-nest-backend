namespace iucs.readernest.domain.Enums
{
    public enum LeaveStatus
    {
        Pending,
        Approved,
        Rejected,
        /// <summary>Withdrawn by the teacher themselves while still Pending — nobody reviewed it.</summary>
        Cancelled
    }
}
