using iucs.readernest.application.Dto.Users;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Common
{
    /// <summary>
    /// Named Sub Admin permission presets: applying one replaces the user's grants
    /// with a role-shaped matrix instead of hand-picking modules.
    /// </summary>
    public static class PermissionPresets
    {
        /// <summary>
        /// Academic Coordinator: scheduling/calendar-scoped — reschedule sessions,
        /// availability checks, holiday and leave visibility with leave approval.
        /// </summary>
        public const string AcademicCoordinator = "academic-coordinator";

        /// <summary>Management: read-only executive dashboard (KPI view only).</summary>
        public const string Management = "management";

        public static IReadOnlyList<string> Names { get; } = [AcademicCoordinator, Management];

        public static IReadOnlyList<PermissionDto>? Resolve(string name)
        {
            return name.ToLowerInvariant() switch
            {
                AcademicCoordinator =>
                [
                    new PermissionDto { Module = PermissionModule.SessionCalendarManagement, CanView = true, CanCreate = true, CanEdit = true },
                    new PermissionDto { Module = PermissionModule.LeaveManagement, CanView = true, CanApprove = true },
                    new PermissionDto { Module = PermissionModule.UserManagement, CanView = true },
                    new PermissionDto { Module = PermissionModule.CourseBatchManagement, CanView = true },
                ],
                Management =>
                [
                    new PermissionDto { Module = PermissionModule.ReportsAnalytics, CanView = true },
                ],
                _ => null,
            };
        }
    }
}
