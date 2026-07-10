namespace iucs.readernest.domain.Enums
{
    /// <summary>
    /// Feature-level actions checked against a Sub Admin's module permissions.
    /// Admins implicitly hold every action on every module.
    /// </summary>
    public enum PermissionAction
    {
        View,
        Create,
        Edit,
        Delete,
        Approve
    }
}
