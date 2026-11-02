namespace iucs.readernest.domain.Enums
{
    /// <summary>
    /// How the teacher says the class went, picked on the mandatory End Class form (client request
    /// 26 Oct 2026). Internal to the admin team -- shown next to the minutes on Payout Approvals,
    /// never sent to parents. Anything other than <see cref="CompletedFully"/> needs a note.
    /// </summary>
    public enum TeacherClassStatus
    {
        CompletedFully,
        EndedEarly,
        StudentJoinedLate,
        StudentLeftEarly,
        StudentAbsent,
        TeacherNetworkIssue,
        StudentNetworkIssue,
        Other
    }
}
