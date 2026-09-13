namespace iucs.readernest.application.Dto.Batches
{
    /// <summary>
    /// Summary of a one-time data repair pass: batches whose <c>ClassSession</c> rows were left
    /// pointing at a previous teacher (generated before <see cref="BatchService.UpdateAsync"/>'s
    /// reassignment cascade existed) get their still-undelivered sessions moved onto whoever the
    /// batch's teacher actually is now. A batch is skipped (and reported here) rather than fixed
    /// if doing so would double-book the new teacher.
    /// </summary>
    public record ReconcileStaleSessionTeachersResultDto(
        int BatchesFixed,
        int SessionsMoved,
        IReadOnlyList<ReconcileStaleSessionTeacherConflictDto> Conflicts);

    public record ReconcileStaleSessionTeacherConflictDto(
        Guid BatchId,
        string BatchName,
        Guid TeacherProfileId,
        string Reason);
}
