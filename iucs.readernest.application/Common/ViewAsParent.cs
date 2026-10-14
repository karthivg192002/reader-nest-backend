namespace iucs.readernest.application.Common
{
    /// <summary>
    /// "View as parent": a staff member who can see the Users directory (every Relationship
    /// Manager, or an Admin) opens a parent's portal exactly as that parent sees
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

        /// <summary>
        /// User Management view -- baseline for every Relationship Manager
        /// (RequiredSystemRolePermissions). It used to be the Edit grant Reset PIN needs, which
        /// RMs don't hold by default: reported live as the three RMs never seeing "View as
        /// parent". The session is read-only, so looking needs no more than the right to look.
        /// </summary>
        public const string RequiredPermission = "UserManagement:View";
    }
}
