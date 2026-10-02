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

        /// <summary>
        /// What a parent may see of a cancelled class's reason. Their own cancellation keeps the
        /// reason they gave; a teacher's cancellation (personal leave, etc.) is reduced to the
        /// fact that the teacher is unavailable, so the teacher's private leave reason and the
        /// approver's name are never shown to a family; anything else keeps its detail but not
        /// the staff member's name.
        /// </summary>
        public static string? ForParent(string? reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                return reason;
            }

            const string prefix = "Cancelled by ";
            if (!reason.StartsWith(prefix, StringComparison.Ordinal))
            {
                return reason; // legacy free-text reason with no "who"
            }

            var rest = reason[prefix.Length..];
            var sep = rest.IndexOf(": ", StringComparison.Ordinal);
            var who = sep == -1 ? rest : rest[..sep];
            var detail = sep == -1 ? string.Empty : rest[(sep + 2)..];

            if (who.EndsWith("(Parent)", StringComparison.Ordinal) || who.StartsWith("every parent", StringComparison.Ordinal))
            {
                return reason;
            }

            if (who.EndsWith("(Teacher)", StringComparison.Ordinal))
            {
                return "Cancelled by Teacher: your teacher is unavailable, so this class has been cancelled.";
            }

            return detail.Length == 0 ? "Cancelled by the academy" : $"Cancelled by the academy: {detail}";
        }

        public static string Reason(string who, string? detail)
        {
            var text = string.IsNullOrWhiteSpace(detail) ? $"Cancelled by {who}" : $"Cancelled by {who}: {detail.Trim()}";
            return text.Length <= MaxReasonLength ? text : text[..MaxReasonLength];
        }
    }
}
