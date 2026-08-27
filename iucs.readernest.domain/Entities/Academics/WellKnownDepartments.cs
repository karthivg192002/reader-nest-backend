namespace iucs.readernest.domain.Entities.Academics
{
    /// <summary>
    /// Fixed ids for the two departments this app shipped with (Phonics, Maths), seeded once
    /// by DatabaseInitializer. Departments are otherwise a plain admin-managed table now (see
    /// <see cref="Department"/>) -- these constants exist only so the small number of "default
    /// to Phonics if nothing else is set" fallbacks (BillingService, EnrollmentService,
    /// BillingBackgroundService) have a stable id to fall back to, the same way the old
    /// `Department.Phonics` enum default worked, without a runtime lookup.
    /// </summary>
    public static class WellKnownDepartments
    {
        public static readonly Guid Phonics = new("00000000-0000-0000-0000-000000000001");
        public static readonly Guid Maths = new("00000000-0000-0000-0000-000000000002");
    }
}
