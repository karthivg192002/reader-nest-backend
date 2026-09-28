namespace iucs.readernest.domain.Enums
{
    /// <summary>
    /// How a non-teacher staff member's monthly pay is worked out. Teachers have their own
    /// batch-wise / per-minute payout system (see Payout/PayoutRate) — this is for Coordinators,
    /// Relationship Managers, Admission Counsellors and Admin.
    /// </summary>
    public enum CompensationBasis
    {
        /// <summary>A flat amount every month (Coordinators, Relationship Managers, Admin).</summary>
        FixedMonthly,

        /// <summary>
        /// A percentage of the money the staff member collected that month, after the total is
        /// floored down to the nearest ₹10,000 (Admission Counsellors).
        /// </summary>
        PercentOfMonthlyCollection,
    }
}
