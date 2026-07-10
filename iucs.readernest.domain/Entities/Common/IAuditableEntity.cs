namespace iucs.readernest.domain.Entities.Common
{
    /// <summary>
    /// Contract for tables that must record which user created/last changed a row.
    /// Applied only to admin-managed or financially sensitive tables; system-generated
    /// high-volume tables (attendance, notifications, logs) stay on <see cref="IBaseEntity"/> alone.
    /// </summary>
    public interface IAuditableEntity : IBaseEntity
    {
        Guid? CreatedBy { get; set; }

        Guid? UpdatedBy { get; set; }
    }
}
