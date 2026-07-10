namespace iucs.readernest.domain.Enums
{
    /// <summary>
    /// Platform roles. Academic Coordinator and Management are Sub Admin permission presets,
    /// not separate roles. Students participate through the Parent account.
    /// </summary>
    public enum UserRole
    {
        Admin,
        SubAdmin,
        AdmissionTeam,
        Teacher,
        Parent
    }
}
