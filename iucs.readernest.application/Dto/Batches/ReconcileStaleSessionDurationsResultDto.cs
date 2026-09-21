namespace iucs.readernest.application.Dto.Batches
{
    /// <summary>
    /// Summary of a one-time data repair pass: batches whose <c>ClassSession</c> rows were left
    /// stamped with a stale end time (generated before <see cref="BatchService.UpdateAsync"/>'s
    /// duration cascade existed, then had their duration edited afterward) get their
    /// still-undelivered sessions' <c>ScheduledEndAtUtc</c> corrected to match the batch's
    /// current effective duration. A batch is skipped (and reported here) rather than fixed if
    /// doing so would double-book its teacher.
    /// </summary>
    public record ReconcileStaleSessionDurationsResultDto(
        int BatchesFixed,
        int SessionsFixed,
        IReadOnlyList<ReconcileStaleSessionDurationConflictDto> Conflicts);

    public record ReconcileStaleSessionDurationConflictDto(
        Guid BatchId,
        string BatchName,
        int EffectiveDurationMinutes,
        string Reason);
}
