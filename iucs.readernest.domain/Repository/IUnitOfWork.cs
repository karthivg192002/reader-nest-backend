using iucs.readernest.domain.Entities.Common;

namespace iucs.readernest.domain.Repository
{
    /// <summary>
    /// Groups repository work into a single transaction boundary; services mutate
    /// through repositories and persist once via <see cref="SaveChangesAsync"/>.
    /// </summary>
    public interface IUnitOfWork
    {
        IRepository<TEntity> Repository<TEntity>() where TEntity : BaseEntity;

        Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    }
}
