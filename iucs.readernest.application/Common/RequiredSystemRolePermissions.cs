using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Common
{
    /// <summary>
    /// Grants a system role must always carry, regardless of how its permissions were last
    /// edited. Single source of truth for two different places that both need it:
    /// DatabaseInitializer's startup backfill (heals an install that's missing one, including
    /// already-assigned Sub Admin users' own snapshot rows) and RoleService.UpdateAsync (stops
    /// the Roles &amp; Permissions screen's replace-all save from silently wiping one back out —
    /// confirmed live: the "management" role kept losing GET /api/courses access every time an
    /// admin saved that preset without the Courses box checked, and the startup-only backfill
    /// only healed it on the next process restart, not the next save).
    /// </summary>
    public static class RequiredSystemRolePermissions
    {
        public static readonly IReadOnlyList<RequiredGrant> All =
        [
            new("teacher", PermissionModule.Payouts, View: true),
            // The Relationship Manager Dashboard's KPI tiles read GET /api/reports/dashboard-summary,
            // gated on this module — the "sub-admin" preset shipped with an empty grant set ("grant
            // modules as needed"), so every Relationship Manager who never had this hand-granted saw
            // every KPI tile fail with "Couldn't load" from the moment they first logged in.
            new("sub-admin", PermissionModule.ReportsAnalytics, View: true),
            // Batches, Users (view) and Audit Log are baseline Relationship Manager access —
            // granted the moment the role is assigned, never gated behind the Admin's
            // Access-Requests approval popup. No Delete flag: BatchesController has no delete
            // endpoint at all (a batch only moves Active/Dormant/Archived via SetStatus, which
            // is PermissionAction.Edit), so "Create/Edit/View/Delete" for batches means
            // Create + Edit + View here, with Edit covering the archive-as-delete action.
            new("sub-admin", PermissionModule.CourseBatchManagement, View: true, Create: true, Edit: true),
            new("sub-admin", PermissionModule.UserManagement, View: true),
            // Lets a teacher see and resolve doubts the "Ask a Doubt" chatbot escalated —
            // Communication already gates Progress Reports/Email Templates for the same module.
            new("teacher", PermissionModule.Communication, View: true, Edit: true),
            new("coordinator", PermissionModule.Communication, View: true, Edit: true),
            // SessionService.IsSessionParticipantAsync's SubAdmin branch requires CanEdit
            // specifically (not just View) before letting a coordinator join a live class as a
            // monitor — the seeded default grants Edit, but nothing protected it from being
            // silently wiped by a preset re-save missing that checkbox, unlike every other
            // required grant here. Confirmed live: a coordinator account could see every class
            // on the calendar fine (View survives) but got 403 "You do not have access to this
            // session" on every single Join Class click, with no per-session pattern to it.
            new("coordinator", PermissionModule.SessionCalendarManagement, View: true, Create: true, Edit: true),
            new("parent", PermissionModule.SessionCalendarManagement, View: true),
            new("parent", PermissionModule.ContentAccessManagement, View: true),
            new("parent", PermissionModule.BillingFinance, View: true),
            new("parent", PermissionModule.Communication, View: true),
            new("admission", PermissionModule.BillingFinance, View: true, Edit: true, Approve: true),
            // The Admission Dashboard's KPI tiles, conversion funnel and "Today & Upcoming
            // Demos" list all read GET /api/sessions, which is gated on this module (see
            // SessionsController.List's [HasPermission] — the [Authorize(Roles=...)] on that
            // endpoint already allows AdmissionTeam, but the permission check still 403's
            // without this). Without it those widgets show "Couldn't load this data" forever,
            // even on a real account with real demos. Confirmed live via network trace:
            // GET /api/sessions?fromUtc=...&toUtc=... → 403 for the admission role.
            new("admission", PermissionModule.SessionCalendarManagement, View: true),
            // The Admission Dashboard's KPI tiles (Demos This Week, Demo->Enrollment Conversion,
            // Pending Follow-ups, Revenue From Conversions) and Conversion Funnel chart all read
            // GET /api/reports/dashboard-summary, gated on this module -- same root cause as the
            // "sub-admin" grant above, just never applied to the AdmissionTeam system role itself.
            // Confirmed live: a real AdmissionTeam account 403'd on this endpoint, showing
            // "Couldn't load" on every KPI tile despite having real demos/leads to show.
            new("admission", PermissionModule.ReportsAnalytics, View: true),
            // /management/revenue's course-wise breakdown reads GET /api/courses, which is
            // gated on this module, not ReportsAnalytics — without it the page's own API call
            // 403's and silently renders "No records found, ₹0 total" instead of the real
            // figures shown by the chart above it.
            new("management", PermissionModule.CourseBatchManagement, View: true),
            // ClassSessionLogs is a net-new module (added alongside the Class Session Logs
            // screen) — SeedRolesAsync's existing-role branch never re-derives a role's
            // Permissions from AllModulesFull() once the row already exists, so an already-
            // seeded "admin" RoleDefinition (every real install) never picked up this module
            // the way a brand-new database's admin role automatically would. Functionally a
            // real Admin *account* was never blocked by this — HasPermissionAttribute passes
            // Admin implicitly, independent of any RolePermission row — but the Roles &
            // Permissions screen's own matrix showed "Class Session Logs" unchecked for the
            // Admin system role while every other module showed fully checked, which read as
            // a real inconsistency/bug. Admin-only; no other system role gets this module by
            // default (Class Session Logs is an IT/ops-monitoring capability, granted per-role
            // like any other custom module — e.g. to a custom "IT Admin" preset — not baseline
            // access for Coordinator/Management/etc.).
            new("admin", PermissionModule.ClassSessionLogs, View: true, Create: true, Edit: true, Delete: true, Approve: true),
        ];

        public sealed record RequiredGrant(
            string RoleName,
            PermissionModule Module,
            bool View = false,
            bool Create = false,
            bool Edit = false,
            bool Delete = false,
            bool Approve = false);
    }
}
