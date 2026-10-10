namespace iucs.readernest.domain.Common
{
    /// <summary>
    /// Abstraction over the authenticated caller, consumed by the audit interceptor.
    /// Implemented in the API layer (claims-based); returns null until authentication
    /// ships in Sprint 1, which the interceptor records as a system action.
    /// </summary>
    public interface ICurrentUserService
    {
        Guid? UserId { get; }

        /// <summary>
        /// During a read-only "view as parent" session, the staff member actually looking
        /// (UserId is then the parent being viewed); null otherwise. Audit rows credit this
        /// person, so the log never shows a parent doing something a staff member did.
        /// </summary>
        Guid? ViewAsActorUserId => null;
    }
}
