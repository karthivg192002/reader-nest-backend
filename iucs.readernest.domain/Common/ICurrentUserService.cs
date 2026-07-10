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
    }
}
