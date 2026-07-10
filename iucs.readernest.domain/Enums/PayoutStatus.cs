namespace iucs.readernest.domain.Enums
{
    /// <summary>
    /// Monthly payout lifecycle: Pending accumulates per-session items during the month,
    /// Finalized locks the month-end total, Paid marks disbursement. Previous months stay intact.
    /// </summary>
    public enum PayoutStatus
    {
        Pending,
        Finalized,
        Paid
    }
}
