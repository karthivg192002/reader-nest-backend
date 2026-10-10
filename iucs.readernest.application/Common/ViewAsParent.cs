namespace iucs.readernest.application.Common
{
    /// <summary>
    /// "View as parent": a staff member who can manage users (a Relationship Manager with
    /// User Management edit, or an Admin) opens a parent's portal exactly as that parent sees
    /// it, to troubleshoot what the parent is reporting -- without ever knowing or asking for
    /// the parent's PIN. The access token it issues is the parent's own (so every parent
    /// screen behaves normally) plus a <see cref="ActorClaimType"/> claim naming who is really
    /// looking. That claim is what makes the session read-only (ViewAsReadOnlyMiddleware), keeps
    /// it short (<see cref="SessionMinutes"/>, never extended by /auth/me's refresh), and puts
    /// the staff member -- not the parent -- in the audit log.
    /// </summary>
    public static class ViewAsParent
    {
        /// <summary>JWT claim carrying the staff member's user id on a view-as token.</summary>
        public const string ActorClaimType = "viewAsBy";

        /// <summary>How long one view-as session lasts before the staff member must start another.</summary>
        public const int SessionMinutes = 30;

        /// <summary>Same grant that already lets someone Reset a parent's PIN.</summary>
        public const string RequiredPermission = "UserManagement:Edit";
    }
}
