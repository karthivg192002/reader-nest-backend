using iucs.readernest.application.Dto.Payouts;
using iucs.readernest.domain.Entities.Payouts;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Mappings
{
    /// <summary>Correction history rows with the name of whoever made each change — shared by teacher and staff payouts.</summary>
    public static class PayoutChangeMapper
    {
        public static async Task<IReadOnlyList<PayoutChangeDto>> ToDtosAsync(
            IUnitOfWork unitOfWork, IReadOnlyList<PayoutChange> changes, CancellationToken cancellationToken)
        {
            var userIds = changes.Where(c => c.CreatedBy != null).Select(c => c.CreatedBy!.Value).Distinct().ToList();
            var names = userIds.Count == 0
                ? new Dictionary<Guid, string>()
                : await unitOfWork.Repository<User>().Query()
                    .Where(u => userIds.Contains(u.Id))
                    .ToDictionaryAsync(u => u.Id, u => (u.FirstName + " " + u.LastName).Trim(), cancellationToken);

            return changes.Select(c => new PayoutChangeDto
            {
                Id = c.Id,
                PayoutItemId = c.PayoutItemId,
                Description = c.Description,
                Reason = c.Reason,
                AmountBefore = c.AmountBefore,
                AmountAfter = c.AmountAfter,
                TotalBefore = c.TotalBefore,
                TotalAfter = c.TotalAfter,
                ChangedByName = c.CreatedBy is { } id && names.TryGetValue(id, out var name) ? name : null,
                ChangedAtUtc = DateTime.SpecifyKind(c.CreatedAtUtc, DateTimeKind.Utc),
            }).ToList();
        }
    }
}
