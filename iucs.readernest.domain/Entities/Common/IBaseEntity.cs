namespace iucs.readernest.domain.Entities.Common
{
    /// <summary>
    /// Contract for identity, lifecycle timestamps and soft-delete state shared by every table.
    /// </summary>
    public interface IBaseEntity
    {
        Guid Id { get; set; }

        DateTime CreatedAtUtc { get; set; }

        DateTime? UpdatedAtUtc { get; set; }

        bool IsDeleted { get; set; }

        DateTime? DeletedAtUtc { get; set; }
    }
}
