namespace iucs.readernest.domain.Entities.Common
{
    /// <summary>
    /// Base for tables requiring a "who did it" audit trail on top of <see cref="BaseEntity"/>.
    /// CreatedBy/UpdatedBy hold the acting user's Id and are populated by the
    /// AuditableEntityInterceptor from ICurrentUserService; null means a system action.
    /// </summary>
    public abstract class AuditEntity : BaseEntity, IAuditableEntity
    {
        public Guid? CreatedBy { get; set; }

        public Guid? UpdatedBy { get; set; }
    }
}
