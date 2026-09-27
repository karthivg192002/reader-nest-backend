using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Common
{
    /// <summary>
    /// Demos are account-specific: an Admission Counselor or a Parent Relationship Manager sees
    /// (and acts on) only the demos they scheduled themselves, never the rest of the team's.
    /// Everyone else (Admin, Management, Coordinator, ...) and system callers keep the full view.
    /// Shared by the demo list (DemoBookingService) and every class list that also shows demos
    /// (SessionService — Sessions, Calendar, dashboards), so a demo can't leak through either.
    /// </summary>
    public static class DemoOwnershipScope
    {
        /// <summary>The RM preset's RoleDefinition.Name. Its DisplayName is "Parent Relationship
        /// Manager" — comparing that against Name never matched, so RMs used to see every demo.</summary>
        public const string RelationshipManagerRoleName = "sub-admin";

        /// <summary>The caller's own user id when their demos are creator-scoped; null for the full view.</summary>
        public static async Task<Guid?> GetAsync(IUnitOfWork unitOfWork, Guid? userId, CancellationToken cancellationToken = default)
        {
            if (userId is null)
            {
                return null;
            }

            var user = await unitOfWork.Repository<User>().Query()
                .Include(u => u.RoleDefinition)
                .FirstOrDefaultAsync(u => u.Id == userId.Value, cancellationToken);
            var scoped = user is not null
                && (user.Role == UserRole.AdmissionTeam
                    || (user.Role == UserRole.SubAdmin
                        && string.Equals(user.RoleDefinition?.Name, RelationshipManagerRoleName, StringComparison.OrdinalIgnoreCase)));
            return scoped ? userId : null;
        }
    }
}
