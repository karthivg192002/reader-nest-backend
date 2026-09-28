using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Common
{
    /// <summary>
    /// Every cancelled class records WHO cancelled it, as the start of its CancellationReason:
    /// "Cancelled by {name} ({role}): {detail}". Reported live: classes cancelled automatically
    /// (teacher leave, a parent's cancellation) showed admin and the founder no way to tell who
    /// had done it.
    /// </summary>
    public static class CancelledBy
    {
        private const int MaxReasonLength = 500;

        /// <summary>"Priya Shah (Teacher)", "Admin", or "System" when nobody is signed in (background jobs).</summary>
        public static async Task<string> DescribeAsync(IUnitOfWork unitOfWork, Guid? userId, CancellationToken cancellationToken = default)
        {
            if (userId is not { } id || id == Guid.Empty)
            {
                return "System";
            }

            var user = await unitOfWork.Repository<User>().Query()
                .Include(u => u.RoleDefinition)
                .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
            return user is null ? "System" : Describe(user);
        }

        public static string Describe(User user)
        {
            var name = $"{user.FirstName} {user.LastName}".Trim();
            var role = user.Role switch
            {
                UserRole.Admin => "Admin",
                UserRole.Teacher => "Teacher",
                UserRole.Parent => "Parent",
                UserRole.AdmissionTeam => "Admission team",
                UserRole.SubAdmin => user.RoleDefinition?.DisplayName ?? "Staff",
                _ => user.Role.ToString(),
            };
            return string.IsNullOrWhiteSpace(name) ? role : $"{name} ({role})";
        }

        public static string Reason(string who, string? detail)
        {
            var text = string.IsNullOrWhiteSpace(detail) ? $"Cancelled by {who}" : $"Cancelled by {who}: {detail.Trim()}";
            return text.Length <= MaxReasonLength ? text : text[..MaxReasonLength];
        }
    }
}
