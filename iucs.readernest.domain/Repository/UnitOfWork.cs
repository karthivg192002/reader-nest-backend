using System.Collections.Concurrent;
using iucs.readernest.domain.Data;
using iucs.readernest.domain.Entities.Common;

namespace iucs.readernest.domain.Repository
{
    public class UnitOfWork : IUnitOfWork
    {
        private readonly ReaderNestDbContext _context;
        private readonly ConcurrentDictionary<Type, object> _repositories = new();

        public UnitOfWork(ReaderNestDbContext context)
        {
            _context = context;
        }

        public IRepository<TEntity> Repository<TEntity>() where TEntity : BaseEntity
        {
            return (IRepository<TEntity>)_repositories.GetOrAdd(
                typeof(TEntity),
                _ => new EfRepository<TEntity>(_context));
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return _context.SaveChangesAsync(cancellationToken);
        }
    }
}
