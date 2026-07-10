namespace iucs.readernest.domain.Entities.Common
{
    /// <summary>
    /// Base for all tables: Guid PK, created/updated timestamps and soft delete.
    /// Timestamps and soft-delete conversion are set automatically by the
    /// AuditableEntityInterceptor on SaveChanges.
    /// </summary>
    public abstract class BaseEntity : IBaseEntity
    {
        public Guid Id { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }

        public bool IsDeleted { get; set; }

        public DateTime? DeletedAtUtc { get; set; }
    }
}
