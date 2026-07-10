using iucs.readernest.domain.Common;
using iucs.readernest.domain.Entities.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace iucs.readernest.domain.Data.Interceptors
{
    /// <summary>
    /// Centralises entity lifecycle bookkeeping so no service ever sets it by hand:
    /// stamps CreatedAtUtc/UpdatedAtUtc on every <see cref="IBaseEntity"/>,
    /// stamps CreatedBy/UpdatedBy on <see cref="IAuditableEntity"/> rows from the
    /// current user, and converts hard deletes into soft deletes.
    /// </summary>
    public sealed class AuditableEntityInterceptor : SaveChangesInterceptor
    {
        private readonly ICurrentUserService _currentUser;

        public AuditableEntityInterceptor(ICurrentUserService currentUser)
        {
            _currentUser = currentUser;
        }

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result)
        {
            ApplyAuditRules(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            ApplyAuditRules(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void ApplyAuditRules(DbContext? context)
        {
            if (context is null)
            {
                return;
            }

            var utcNow = DateTime.UtcNow;
            var userId = _currentUser.UserId;

            foreach (var entry in context.ChangeTracker.Entries<IBaseEntity>())
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        entry.Entity.CreatedAtUtc = utcNow;
                        if (entry.Entity is IAuditableEntity added)
                        {
                            added.CreatedBy = userId;
                        }
                        break;

                    case EntityState.Modified:
                        entry.Entity.UpdatedAtUtc = utcNow;
                        if (entry.Entity is IAuditableEntity modified)
                        {
                            modified.UpdatedBy = userId;
                        }
                        break;

                    case EntityState.Deleted:
                        entry.State = EntityState.Modified;
                        entry.Entity.IsDeleted = true;
                        entry.Entity.DeletedAtUtc = utcNow;
                        entry.Entity.UpdatedAtUtc = utcNow;
                        if (entry.Entity is IAuditableEntity deleted)
                        {
                            deleted.UpdatedBy = userId;
                        }
                        break;
                }
            }
        }
    }
}
