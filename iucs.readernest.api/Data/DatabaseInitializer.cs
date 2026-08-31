using System.Text.Json;
using iucs.readernest.application.Common;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Dto.Users;
using iucs.readernest.domain.Data;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Communication;
using iucs.readernest.domain.Entities.Integrations;
using iucs.readernest.domain.Entities.Navigation;
using iucs.readernest.domain.Entities.Settings;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.api.Data
{
    /// <summary>
    /// Startup bootstrap: applies pending migrations and seeds the first admin
    /// account plus the two department payment accounts. Controlled by
    /// "Database:MigrateOnStartup" and the "Seed" configuration section.
    /// </summary>
    public static class DatabaseInitializer
    {
        public static async Task InitializeAsync(IServiceProvider services, IConfiguration configuration)
        {
            using var scope = services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ReaderNestDbContext>();

            if (configuration.GetValue<bool>("Database:MigrateOnStartup"))
            {
                await context.Database.MigrateAsync();
            }

            await SeedAdminAsync(scope.ServiceProvider, context, configuration);
            await EnsureAdminPinAsync(scope.ServiceProvider, context, configuration);
            await SeedDepartmentsAsync(context);
            await SeedPaymentAccountsAsync(context);
            await SeedSettingsAsync(context);
            await SeedRolesAsync(context);
            await BackfillSystemRolePermissionsAsync(context);
            await SeedMenusAsync(context);
            await RemoveRetiredMenusAsync(context);
            await EnsureSubAdminIntegrationsMenuAsync(context);
            await EnsurePackagesAndStudentViewMenusAsync(context);
            await EnsureAdminDepartmentsMenuAsync(context);
            await EnsureAdminQuizBankMenuAsync(context);
            await EnsureAdminActivityBankMenuAsync(context);
            await EnsureAdminServerMonitoringMenuAsync(context);
            await EnsureBulkEmailHistoryMenuAsync(context);
            await BackfillMenuRequiredModulesAsync(context);
            await SeedIntegrationsAsync(context);
            await EnsureCashPaymentMethodAsync(context);
            await EnsureSmsIntegrationAsync(context);
            await EnsureJitsiAutoRecordConfigAsync(context);
            await SeedEmailTemplatesAsync(context);
            await ReconcileJoinLinkEmailTemplatesAsync(context);
            await ReconcileWelcomeCredentialsPinTemplateAsync(context);
            await EnsureEmailTemplatesMenuAsync(context);
            await EnsureProgressReportEmailTemplateAsync(context);
            await EnsurePinResetEmailTemplateAsync(context);
            await EnsureAccessRequestEmailTemplatesAsync(context);
            await ReconcileOrgNameEmailTemplatesAsync(context);
            await EnsureProgressReportsMenuAsync(context);
            await EnsureStoreInquiriesMenuAsync(context);
            await EnsureParentRecordingsMenuAsync(context);
            await EnsureChatbotMenusAsync(context);
            await SeedChatFaqsAsync(context);
            await EnsureAdditionalChatFaqsAsync(context);
            await BackfillPlainTextNotificationBodiesAsync(context);

            await context.SaveChangesAsync();
        }

        private static async Task SeedAdminAsync(
            IServiceProvider services,
            ReaderNestDbContext context,
            IConfiguration configuration)
        {
            var email = configuration["Seed:AdminEmail"];
            var pin = configuration["Seed:AdminPin"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(pin))
            {
                return;
            }

            if (await context.Users.AnyAsync(u => u.Role == UserRole.Admin))
            {
                return;
            }

            var hasher = services.GetRequiredService<IPasswordHasher>();
            context.Users.Add(new User
            {
                Email = email.Trim().ToLowerInvariant(),
                PinHash = hasher.Hash(pin),
                FirstName = configuration["Seed:AdminFirstName"] ?? "Meet to Manage",
                LastName = configuration["Seed:AdminLastName"] ?? "Admin",
                Role = UserRole.Admin,
            });
        }

        /// <summary>
        /// Migration for the PIN-login switch: keeps the seeded admin account's PinHash
        /// converged on Seed:AdminPin on every startup. Originally this only fired when the
        /// hash still verified against the old Seed:AdminPassword (a stricter one-time
        /// migration), but that requires this value to exactly match whatever the account
        /// was actually last hashed from — unverifiable from here, and got the account
        /// locked out in practice when it didn't line up. Unconditional convergence trades
        /// away "never overwrites a PIN changed since" for "the documented seed PIN always
        /// works," which matters more while there's no self-service PIN change yet.
        /// </summary>
        private static async Task EnsureAdminPinAsync(
            IServiceProvider services,
            ReaderNestDbContext context,
            IConfiguration configuration)
        {
            var email = configuration["Seed:AdminEmail"];
            var newPin = configuration["Seed:AdminPin"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(newPin))
            {
                return;
            }

            var admin = context.Users.Local.FirstOrDefault(u => u.Email == email.Trim().ToLowerInvariant() && u.Role == UserRole.Admin)
                ?? await context.Users.FirstOrDefaultAsync(u => u.Email == email.Trim().ToLowerInvariant() && u.Role == UserRole.Admin);
            if (admin is null)
            {
                return;
            }

            var hasher = services.GetRequiredService<IPasswordHasher>();
            if (!hasher.Verify(newPin, admin.PinHash))
            {
                admin.PinHash = hasher.Hash(newPin);
            }
        }

        /// <summary>
        /// Departments used to be a fixed 2-value enum (Phonics, Maths); now they're an
        /// admin-manageable table (Settings/Departments screen can add more, e.g. Hindi,
        /// Abacus, Spoken English) but the two original ones still seed under fixed, known
        /// ids (<see cref="WellKnownDepartments"/>) so the handful of "default to Phonics"
        /// fallbacks elsewhere (BillingService, EnrollmentService) have a stable id to use.
        /// </summary>
        private static async Task SeedDepartmentsAsync(ReaderNestDbContext context)
        {
            if (await context.Departments.AnyAsync())
            {
                return;
            }

            context.Departments.AddRange(
                new Department { Id = WellKnownDepartments.Phonics, Name = "Phonics", IsActive = true },
                new Department { Id = WellKnownDepartments.Maths, Name = "Maths", IsActive = true });
        }

        private static async Task SeedPaymentAccountsAsync(ReaderNestDbContext context)
        {
            if (await context.PaymentAccounts.AnyAsync())
            {
                return;
            }

            // Defaults match the dual-gateway requirement (Phonics -> Razorpay, Maths -> Cashfree).
            // These only route live once the matching Settings -> Integrations record is enabled
            // with real credentials; until then PaymentGatewayDispatcher falls back to the
            // simulated gateway. Admin can repoint either account from Payment Gateway Mapping.
            context.PaymentAccounts.AddRange(
                new PaymentAccount
                {
                    Name = "Phonics Department Account",
                    DepartmentId = WellKnownDepartments.Phonics,
                    GatewayProvider = "razorpay",
                    GatewayAccountRef = "phonics-account",
                },
                new PaymentAccount
                {
                    Name = "Maths Department Account",
                    DepartmentId = WellKnownDepartments.Maths,
                    GatewayProvider = "cashfree",
                    GatewayAccountRef = "maths-account",
                });
        }

        private static async Task SeedSettingsAsync(ReaderNestDbContext context)
        {
            if (await context.AppSettings.AnyAsync())
            {
                return;
            }

            AppSetting Setting(SettingCategory category, string key, string? value, bool isPublic = false) =>
                new() { Category = category, Key = key, Value = value, IsPublic = isPublic };

            context.AppSettings.AddRange(
                Setting(SettingCategory.General, "org.name", "Meet to Manage", isPublic: true),
                Setting(SettingCategory.General, "org.domain", "app.meettomanage.cloud"),
                Setting(SettingCategory.General, "org.supportEmail", "support@meettomanage.cloud"),
                Setting(SettingCategory.General, "org.supportPhone", "+91 98200 00000"),
                Setting(SettingCategory.General, "org.timezone", "Asia/Kolkata (GMT +5:30)"),
                Setting(SettingCategory.Branding, "brand.name", "Meet to Manage", isPublic: true),
                Setting(SettingCategory.Branding, "brand.logoUrl", null, isPublic: true),
                Setting(SettingCategory.Branding, "brand.primaryColor", "#1E3A5F", isPublic: true),
                Setting(SettingCategory.Branding, "brand.accentColor", "#E63329", isPublic: true),
                Setting(SettingCategory.Notifications, "notify.feeReminders", "true"),
                Setting(SettingCategory.Notifications, "notify.leaveRequests", "true"),
                Setting(SettingCategory.Notifications, "notify.lowAttendance", "false"),
                Setting(SettingCategory.Notifications, "notify.weeklyDigest", "true"));
        }

        private static async Task SeedRolesAsync(ReaderNestDbContext context)
        {
            // System roles mirror the platform's portals so the Roles & Permissions
            // screen ships with a ready-to-assign preset per persona. Reconciled on
            // every start (insert-if-absent + retire renamed/obsolete system roles),
            // never clobbering admin-created custom roles or hand-edited matrices.
            var desired = SystemRoleSeeds();
            var desiredNames = desired.Select(d => d.Name).ToHashSet();

            var existing = await context.RoleDefinitions
                .Include(r => r.Permissions)
                .ToListAsync();
            var existingByName = existing.ToDictionary(r => r.Name);

            foreach (var seed in desired)
            {
                if (existingByName.TryGetValue(seed.Name, out var current))
                {
                    // Backfill the default landing route on roles seeded before the
                    // column existed; leave the permission matrix the admin may have edited.
                    if (string.IsNullOrWhiteSpace(current.DefaultRoute))
                    {
                        current.DefaultRoute = seed.DefaultRoute;
                    }

                    // Keep system roles' display name/description in sync with the seed
                    // (e.g. the Sub Admin → Parent Relationship Manager rename).
                    if (current.IsSystem)
                    {
                        current.DisplayName = seed.DisplayName;
                        current.Description = seed.Description;
                    }

                    continue;
                }

                context.RoleDefinitions.Add(new RoleDefinition
                {
                    Name = seed.Name,
                    DisplayName = seed.DisplayName,
                    Description = seed.Description,
                    DefaultRoute = seed.DefaultRoute,
                    IsSystem = true,
                    Permissions = seed.Permissions.Select(p => new RolePermission
                    {
                        Module = p.Module,
                        CanView = p.CanView,
                        CanCreate = p.CanCreate,
                        CanEdit = p.CanEdit,
                        CanDelete = p.CanDelete,
                        CanApprove = p.CanApprove,
                    }).ToList(),
                });
            }

            // Retire obsolete system roles (e.g. the old "academic-coordinator",
            // replaced by "coordinator"). Clear any user assignment first so the
            // Restrict FK doesn't block the delete; the user's own permission grants
            // are untouched, only the named-role pointer is reset.
            var obsolete = existing.Where(r => r.IsSystem && !desiredNames.Contains(r.Name)).ToList();
            foreach (var role in obsolete)
            {
                var assignedUsers = await context.Users
                    .Where(u => u.RoleDefinitionId == role.Id)
                    .ToListAsync();
                foreach (var user in assignedUsers)
                {
                    user.RoleDefinitionId = null;
                }

                context.RolePermissions.RemoveRange(role.Permissions);
                context.RoleDefinitions.Remove(role);
            }
        }

        /// <summary>
        /// Additive-only upgrade for system roles seeded before a given module was part
        /// of their default grant (e.g. Admission gaining Billing &amp; Finance for cash
        /// confirmation). Only inserts a module row the role doesn't already have — an
        /// admin who has since edited/removed that module's grant is never overwritten.
        /// </summary>
        private static async Task BackfillSystemRolePermissionsAsync(ReaderNestDbContext context)
        {
            var additions = RequiredSystemRolePermissions.All;

            var roleNames = additions.Select(a => a.RoleName).Distinct().ToList();
            var roles = await context.RoleDefinitions
                .Include(r => r.Permissions)
                .Where(r => roleNames.Contains(r.Name))
                .ToListAsync();

            // Sub Admin personas (Academic Coordinator, Management, ...) don't read this
            // RoleDefinition live — LoadPermissionClaimsAsync resolves every Sub Admin's
            // access from their own SubAdminPermission rows, a snapshot copied in once when
            // the preset was assigned (deliberately editable per-person after that; see
            // AuthService.LoadPermissionClaimsAsync's own comment). Fixing the shared preset
            // above does nothing for someone who was already assigned it before the fix
            // shipped — confirmed the actual reason a Management-role test account kept
            // 403ing on GET /api/courses even after this grant existed on the "management"
            // RoleDefinition. Preloaded once for every role in `additions`, even non-Sub-Admin
            // ones (teacher/parent/admission), where this query simply returns nothing since
            // no user is both that role's assignee and UserRole.SubAdmin.
            var roleIds = roles.Select(r => r.Id).ToList();
            var subAdminUserIds = await context.Users
                .Where(u => u.Role == UserRole.SubAdmin && u.RoleDefinitionId.HasValue && roleIds.Contains(u.RoleDefinitionId.Value))
                .Select(u => new { u.Id, u.RoleDefinitionId })
                .ToListAsync();
            var existingSubAdminGrants = await context.SubAdminPermissions
                .Where(p => subAdminUserIds.Select(u => u.Id).Contains(p.UserId))
                .ToListAsync();

            foreach (var (roleName, module, view, create, edit, delete, approve) in additions)
            {
                var role = roles.FirstOrDefault(r => r.Name == roleName);
                if (role is null)
                {
                    continue;
                }

                var existing = role.Permissions.FirstOrDefault(p => p.Module == module);
                if (existing is null)
                {
                    context.RolePermissions.Add(new RolePermission
                    {
                        RoleDefinitionId = role.Id,
                        Module = module,
                        CanView = view,
                        CanCreate = create,
                        CanEdit = edit,
                        CanDelete = delete,
                        CanApprove = approve,
                    });
                }
                else
                {
                    // A row already existing here isn't proof this addition already landed — the
                    // Permissions screen saves its whole matrix on every edit (RoleService.UpdateAsync),
                    // so an admin saving that role for an unrelated reason, without this module's box
                    // checked, creates exactly this row with every flag false. Skipping whenever *a*
                    // row exists (the previous check) meant that row permanently blocked this addition
                    // from ever taking effect — confirmed live: Management kept 403ing on GET
                    // /api/courses after this grant shipped, because a prior Permissions save had
                    // already created a CanView=false row for it. OR-in only the flags this addition
                    // asks for, so it can't revoke something an admin deliberately granted elsewhere.
                    existing.CanView = existing.CanView || view;
                    existing.CanCreate = existing.CanCreate || create;
                    existing.CanEdit = existing.CanEdit || edit;
                    existing.CanDelete = existing.CanDelete || delete;
                    existing.CanApprove = existing.CanApprove || approve;
                }

                foreach (var userId in subAdminUserIds.Where(u => u.RoleDefinitionId == role.Id).Select(u => u.Id))
                {
                    var grant = existingSubAdminGrants.FirstOrDefault(p => p.UserId == userId && p.Module == module);
                    if (grant is null)
                    {
                        context.SubAdminPermissions.Add(new SubAdminPermission
                        {
                            UserId = userId,
                            Module = module,
                            CanView = view,
                            CanCreate = create,
                            CanEdit = edit,
                            CanDelete = delete,
                            CanApprove = approve,
                        });
                    }
                    else
                    {
                        grant.CanView = grant.CanView || view;
                        grant.CanCreate = grant.CanCreate || create;
                        grant.CanEdit = grant.CanEdit || edit;
                        grant.CanDelete = grant.CanDelete || delete;
                        grant.CanApprove = grant.CanApprove || approve;
                    }
                }
            }
        }

        private static IReadOnlyList<(string Name, string DisplayName, string Description, string DefaultRoute, PermissionDto[] Permissions)> SystemRoleSeeds()
        {
            PermissionDto Grant(PermissionModule module, bool view = false, bool create = false, bool edit = false, bool delete = false, bool approve = false) =>
                new() { Module = module, CanView = view, CanCreate = create, CanEdit = edit, CanDelete = delete, CanApprove = approve };

            PermissionDto[] AllModulesFull() =>
                Enum.GetValues<PermissionModule>()
                    .Select(m => Grant(m, view: true, create: true, edit: true, delete: true, approve: true))
                    .ToArray();

            return
            [
                ("admin", "Admin", "Full access across every module.", "/admin", AllModulesFull()),
                ("teacher", "Teacher", "Class delivery: own schedule, content and leave.", "/teacher",
                [
                    Grant(PermissionModule.SessionCalendarManagement, view: true),
                    Grant(PermissionModule.ContentAccessManagement, view: true),
                    Grant(PermissionModule.LeaveManagement, view: true),
                    Grant(PermissionModule.Payouts, view: true),
                ]),
                ("parent", "Parent", "Family account holder; managed through the parent portal.", "/parent",
                [
                    Grant(PermissionModule.SessionCalendarManagement, view: true),
                    Grant(PermissionModule.ContentAccessManagement, view: true),
                    Grant(PermissionModule.BillingFinance, view: true),
                    Grant(PermissionModule.Communication, view: true),
                ]),
                ("sub-admin", "Parent Relationship Manager", "Parent relationship management account; grant modules as needed.", "/subadmin", []),
                ("admission", "Admission", "Demo-to-enrollment pipeline and lead follow-up.", "/admission",
                [
                    Grant(PermissionModule.Admission, view: true, create: true, edit: true, approve: true),
                    Grant(PermissionModule.UserManagement, view: true),
                    Grant(PermissionModule.ReportsAnalytics, view: true),
                    // Payment Tracking + cash confirmation: Approve gates the "confirm collected" action itself.
                    Grant(PermissionModule.BillingFinance, view: true, edit: true, approve: true),
                ]),
                ("coordinator", "Coordinator", "Scheduling and calendar coordination with leave approval.", "/coordinator",
                [
                    Grant(PermissionModule.SessionCalendarManagement, view: true, create: true, edit: true),
                    Grant(PermissionModule.LeaveManagement, view: true, approve: true),
                    Grant(PermissionModule.UserManagement, view: true),
                    Grant(PermissionModule.CourseBatchManagement, view: true),
                ]),
                ("management", "Management", "Read-only executive dashboards and reports.", "/management",
                [
                    Grant(PermissionModule.ReportsAnalytics, view: true),
                    // /management/revenue's course-wise breakdown reads GET /api/courses,
                    // which is gated on this module, not ReportsAnalytics — without it the
                    // page's own API call 403'd and silently rendered "No records found,
                    // ₹0 total" instead of the real figures shown by the chart above it.
                    Grant(PermissionModule.CourseBatchManagement, view: true),
                ]),
                ("student", "Student", "Learner experience surfaced through the parent account.", "/student", []),
            ];
        }

        /// <summary>
        /// Removes menu items retired after the initial seed (the seed early-returns once
        /// any menu exists, so removals need their own idempotent pass). Currently drops the
        /// Coordinator "Scheduling" screen — the coordinator role is monitor-only.
        /// </summary>
        private static async Task RemoveRetiredMenusAsync(ReaderNestDbContext context)
        {
            var retiredPaths = new[] { "/coordinator/scheduling" };
            var stale = await context.MenuItems.Where(m => retiredPaths.Contains(m.Path)).ToListAsync();
            if (stale.Count > 0)
            {
                context.MenuItems.RemoveRange(stale);
            }
        }

        /// <summary>
        /// (portal, section, label, path, lucide icon, required module); orders derive from
        /// array position. Shared by the first-boot seed and the existing-database backfill
        /// so the module mapping lives in exactly one place. A null module means the item is
        /// always visible (dashboards and other mandatory, non-delegable actions); Admin
        /// bypasses gating entirely regardless of what's set here.
        /// </summary>
        private static (string Portal, string? Section, string Label, string Path, string Icon, PermissionModule? RequiredModule)[] MenuSeedItems() =>
        [
            ("admin", null, "Dashboard", "/admin", "LayoutDashboard", null),
            ("admin", "Academics", "Courses", "/admin/courses", "BookOpen", PermissionModule.CourseBatchManagement),
            ("admin", "Academics", "Departments", "/admin/departments", "Building2", PermissionModule.CourseBatchManagement),
            ("admin", "Academics", "Batches", "/admin/batches", "Layers", PermissionModule.CourseBatchManagement),
            ("admin", "Academics", "Academic Calendar", "/admin/calendar", "CalendarDays", PermissionModule.SessionCalendarManagement),
            ("admin", "Academics", "Sessions", "/admin/sessions", "CalendarClock", PermissionModule.SessionCalendarManagement),
            ("admin", "Academics", "Quiz Bank", "/admin/quiz-bank", "Sparkles", PermissionModule.CourseBatchManagement),
            ("admin", "People", "Users", "/admin/users", "Users", PermissionModule.UserManagement),
            ("admin", "People", "Roles & Permissions", "/admin/permissions", "ShieldCheck", PermissionModule.UserManagement),
            ("admin", "People", "Enrollment Review", "/admin/enrollments", "ClipboardCheck", PermissionModule.Admission),
            ("admin", "People", "Store Inquiries", "/admin/store-inquiries", "ShoppingBag", PermissionModule.Admission),
            ("admin", "Content", "Content & Resources", "/admin/resources", "FolderOpen", PermissionModule.ContentAccessManagement),
            ("admin", "Finance", "Billing & Finance", "/admin/billing", "Receipt", PermissionModule.BillingFinance),
            ("admin", "Finance", "Packages & Subscriptions", "/admin/packages", "CreditCard", PermissionModule.BillingFinance),
            ("admin", "Finance", "Payment Gateway Mapping", "/admin/payment-mapping", "Landmark", PermissionModule.BillingFinance),
            ("admin", "Finance", "Teacher Payouts", "/admin/payouts", "Wallet", PermissionModule.Payouts),
            ("admin", "Finance", "Fee Suspension", "/admin/fee-suspension", "Ban", PermissionModule.BillingFinance),
            ("admin", "Insights", "Reports & Analytics", "/admin/reports", "BarChart3", PermissionModule.ReportsAnalytics),
            ("admin", "Insights", "Bulk Email", "/admin/bulk-email", "Mail", PermissionModule.Communication),
            ("admin", "Insights", "Bulk Email History", "/admin/bulk-email/history", "History", PermissionModule.Communication),
            ("admin", "Insights", "Email Templates", "/admin/email-templates", "FileText", PermissionModule.Communication),
            ("admin", "Insights", "Progress Reports", "/admin/progress-reports", "ScrollText", PermissionModule.Communication),
            ("admin", "Insights", "Doubt Chatbot", "/admin/chatbot", "MessageCircleQuestion", PermissionModule.Communication),
            ("admin", "System", "Settings & Branding", "/admin/settings", "Settings", PermissionModule.Settings),
            ("teacher", null, "Dashboard", "/teacher", "LayoutDashboard", null),
            ("teacher", "Teaching", "My Classes", "/teacher/classes", "CalendarClock", PermissionModule.SessionCalendarManagement),
            ("teacher", "Teaching", "Live Classroom", "/teacher/live/s-1", "Video", PermissionModule.SessionCalendarManagement),
            ("teacher", "Teaching", "Attendance & Records", "/teacher/attendance", "ClipboardList", PermissionModule.SessionCalendarManagement),
            ("teacher", "Teaching", "Demo Feedback", "/teacher/demo-feedback", "ClipboardCheck", PermissionModule.SessionCalendarManagement),
            ("teacher", "Teaching", "Student Doubts", "/teacher/doubts", "MessageCircleQuestion", PermissionModule.Communication),
            ("teacher", "My Account", "Leave Management", "/teacher/leave", "CalendarOff", PermissionModule.LeaveManagement),
            ("teacher", "My Account", "My Payout", "/teacher/payout", "Banknote", PermissionModule.Payouts),
            ("teacher", "My Account", "Resources", "/teacher/resources", "FolderOpen", PermissionModule.ContentAccessManagement),
            ("parent", null, "Dashboard", "/parent", "LayoutDashboard", null),
            ("parent", "Learning", "Schedule & Live Class", "/parent/schedule", "CalendarClock", PermissionModule.SessionCalendarManagement),
            ("parent", "Learning", "Resources", "/parent/resources", "FolderOpen", PermissionModule.ContentAccessManagement),
            ("parent", "Learning", "Recordings", "/parent/recordings", "Video", PermissionModule.ContentAccessManagement),
            ("parent", "Learning", "Student View", "/student", "Sparkles", null),
            ("parent", "Account", "Payments & Billing", "/parent/billing", "CreditCard", PermissionModule.BillingFinance),
            ("parent", "Account", "Notifications & Reports", "/parent/notifications", "Bell", PermissionModule.Communication),
            ("parent", "Account", "Add Child", "/parent/add-child", "UserPlus", null),
            ("subadmin", null, "Dashboard", "/subadmin", "LayoutDashboard", null),
            ("subadmin", "Access", "My Permissions", "/subadmin/permissions", "ShieldCheck", null),
            ("subadmin", "Access", "Integrations", "/subadmin/integrations", "Plug", PermissionModule.Settings),
            ("subadmin", "Delegated Work", "Assigned Reports", "/subadmin/reports", "BarChart3", PermissionModule.ReportsAnalytics),
            ("subadmin", "Delegated Work", "Audit Log", "/subadmin/audit-log", "History", null),
            ("admission", null, "Dashboard", "/admission", "LayoutDashboard", null),
            ("admission", "Pipeline", "Demo Scheduling", "/admission/demo-scheduling", "CalendarClock", PermissionModule.Admission),
            ("admission", "Pipeline", "Demo Feedback", "/admission/demo-feedback", "ClipboardCheck", PermissionModule.Admission),
            ("admission", "Pipeline", "Conversion Board", "/admission/conversion", "KanbanSquare", PermissionModule.Admission),
            ("admission", "CRM", "Leads & Parents", "/admission/leads", "UserSearch", PermissionModule.Admission),
            ("admission", "CRM", "Payment Tracking", "/admission/payments", "Link2", PermissionModule.BillingFinance),
            ("admission", "Insights", "Reports", "/admission/reports", "BarChart3", PermissionModule.ReportsAnalytics),
            ("coordinator", null, "Dashboard", "/coordinator", "LayoutDashboard", null),
            ("coordinator", "Monitoring", "Academic Calendar", "/coordinator/calendar", "CalendarDays", PermissionModule.SessionCalendarManagement),
            ("coordinator", "Monitoring", "Teacher Availability", "/coordinator/availability", "CalendarRange", PermissionModule.SessionCalendarManagement),
            ("management", null, "Executive Overview", "/management", "LayoutDashboard", null),
            ("management", "Performance", "Revenue & Courses", "/management/revenue", "TrendingUp", PermissionModule.ReportsAnalytics),
            ("management", "Performance", "Teacher & Batch Performance", "/management/performance", "Gauge", PermissionModule.ReportsAnalytics),
            ("management", "Insights", "Reports", "/management/reports", "FileBarChart", PermissionModule.ReportsAnalytics),
            ("student", null, "My Learning", "/student", "Sparkles", null),
        ];

        /// <summary>
        /// Additive-only upgrade for menu items seeded before they carried a module gate:
        /// sets RequiredModule from the canonical mapping above, but only where it's still
        /// null — an admin who has since cleared or repointed an item's gate is never
        /// overwritten. New (portal, path) rows found here that don't exist yet are ignored;
        /// item creation is SeedMenusAsync's job, this only patches gating on existing rows.
        /// </summary>
        private static async Task BackfillMenuRequiredModulesAsync(ReaderNestDbContext context)
        {
            var existing = await context.MenuItems.ToListAsync();
            foreach (var (portal, _, _, path, _, requiredModule) in MenuSeedItems())
            {
                if (requiredModule is null)
                {
                    continue;
                }

                var item = existing.FirstOrDefault(m => m.Portal == portal && m.Path == path);
                if (item is not null && item.RequiredModule is null)
                {
                    item.RequiredModule = requiredModule;
                }
            }
        }

        /// <summary>
        /// Inserts the Sub Admin "Integrations" menu item into a database that was seeded
        /// before this screen existed (SeedMenusAsync only ever creates rows once). Placed
        /// right after "My Permissions" in the "Access" section, nudging Reports/Audit Log
        /// down a slot so nothing collides.
        /// </summary>
        private static async Task EnsureSubAdminIntegrationsMenuAsync(ReaderNestDbContext context)
        {
            const string path = "/subadmin/integrations";
            // On a fresh database SeedMenusAsync has already queued this row in the change
            // tracker (nothing is saved until the single SaveChangesAsync at the end), so a
            // database-only check would insert it twice (23505 on ix_menu_items_portal_path).
            if (context.MenuItems.Local.Any(m => m.Portal == "subadmin" && m.Path == path) ||
                await context.MenuItems.AnyAsync(m => m.Portal == "subadmin" && m.Path == path))
            {
                return;
            }

            var delegatedWork = await context.MenuItems
                .Where(m => m.Portal == "subadmin" && m.Section == "Delegated Work")
                .ToListAsync();
            foreach (var item in delegatedWork)
            {
                item.SortOrder += 1;
            }

            context.MenuItems.Add(new MenuItem
            {
                Portal = "subadmin",
                Section = "Access",
                SectionOrder = 1,
                Label = "Integrations",
                Path = path,
                Icon = "Plug",
                SortOrder = 1,
                IsActive = true,
                RequiredModule = PermissionModule.Settings,
            });
        }

        /// <summary>
        /// Inserts the Admin "Packages &amp; Subscriptions" and Parent "Student View" menu
        /// items into databases seeded before those screens existed (SeedMenusAsync only
        /// ever creates rows once). Idempotent: each insert is skipped when the row exists.
        /// </summary>
        private static async Task EnsurePackagesAndStudentViewMenusAsync(ReaderNestDbContext context)
        {
            // Local checks mirror EnsureSubAdminIntegrationsMenuAsync: on a fresh database
            // these rows are already pending in the change tracker from SeedMenusAsync.
            if (!context.MenuItems.Local.Any(m => m.Portal == "admin" && m.Path == "/admin/packages") &&
                !await context.MenuItems.AnyAsync(m => m.Portal == "admin" && m.Path == "/admin/packages"))
            {
                // Slot directly after "Billing & Finance"; push the rest of Finance down one.
                var billing = await context.MenuItems
                    .FirstOrDefaultAsync(m => m.Portal == "admin" && m.Path == "/admin/billing");
                var financeItems = await context.MenuItems
                    .Where(m => m.Portal == "admin" && m.Section == "Finance")
                    .ToListAsync();
                var insertAt = (billing?.SortOrder ?? -1) + 1;
                foreach (var item in financeItems.Where(m => m.SortOrder >= insertAt))
                {
                    item.SortOrder += 1;
                }

                context.MenuItems.Add(new MenuItem
                {
                    Portal = "admin",
                    Section = "Finance",
                    SectionOrder = billing?.SectionOrder ?? 3,
                    Label = "Packages & Subscriptions",
                    Path = "/admin/packages",
                    Icon = "CreditCard",
                    SortOrder = insertAt,
                    IsActive = true,
                    RequiredModule = PermissionModule.BillingFinance,
                });
            }

            if (!context.MenuItems.Local.Any(m => m.Portal == "parent" && m.Path == "/student") &&
                !await context.MenuItems.AnyAsync(m => m.Portal == "parent" && m.Path == "/student"))
            {
                var learningItems = await context.MenuItems
                    .Where(m => m.Portal == "parent" && m.Section == "Learning")
                    .ToListAsync();

                context.MenuItems.Add(new MenuItem
                {
                    Portal = "parent",
                    Section = "Learning",
                    SectionOrder = learningItems.FirstOrDefault()?.SectionOrder ?? 1,
                    Label = "Student View",
                    Path = "/student",
                    Icon = "Sparkles",
                    SortOrder = learningItems.Count == 0 ? 0 : learningItems.Max(m => m.SortOrder) + 1,
                    IsActive = true,
                    RequiredModule = null,
                });
            }
        }

        /// <summary>
        /// Inserts the Admin "Departments" menu item into a database that was seeded before
        /// the dynamic Department feature existed (SeedMenusAsync only ever creates rows
        /// once). Slotted directly after "Courses" in Academics, nudging Batches/Calendar/
        /// Sessions down a slot so nothing collides. Mirrors EnsureSubAdminIntegrationsMenuAsync.
        /// </summary>
        private static async Task EnsureAdminDepartmentsMenuAsync(ReaderNestDbContext context)
        {
            const string path = "/admin/departments";
            if (context.MenuItems.Local.Any(m => m.Portal == "admin" && m.Path == path) ||
                await context.MenuItems.AnyAsync(m => m.Portal == "admin" && m.Path == path))
            {
                return;
            }

            var courses = await context.MenuItems
                .FirstOrDefaultAsync(m => m.Portal == "admin" && m.Path == "/admin/courses");
            var academicsItems = await context.MenuItems
                .Where(m => m.Portal == "admin" && m.Section == "Academics")
                .ToListAsync();
            var insertAt = (courses?.SortOrder ?? -1) + 1;
            foreach (var item in academicsItems.Where(m => m.SortOrder >= insertAt))
            {
                item.SortOrder += 1;
            }

            context.MenuItems.Add(new MenuItem
            {
                Portal = "admin",
                Section = "Academics",
                SectionOrder = courses?.SectionOrder ?? 1,
                Label = "Departments",
                Path = path,
                Icon = "Building2",
                SortOrder = insertAt,
                IsActive = true,
                RequiredModule = PermissionModule.CourseBatchManagement,
            });
        }

        /// <summary>
        /// Inserts the Admin "Quiz Bank" menu item into a database that was seeded before the
        /// admin-authored quiz bank existed (SeedMenusAsync only ever creates rows once).
        /// Appended after whatever the Academics section's last item currently is, rather than
        /// shifted in like EnsureAdminDepartmentsMenuAsync — nothing after it in that section
        /// needs to move.
        /// </summary>
        private static async Task EnsureAdminQuizBankMenuAsync(ReaderNestDbContext context)
        {
            const string path = "/admin/quiz-bank";
            if (context.MenuItems.Local.Any(m => m.Portal == "admin" && m.Path == path) ||
                await context.MenuItems.AnyAsync(m => m.Portal == "admin" && m.Path == path))
            {
                return;
            }

            var academicsItems = await context.MenuItems
                .Where(m => m.Portal == "admin" && m.Section == "Academics")
                .ToListAsync();
            if (academicsItems.Count == 0)
            {
                return; // no Academics section at all (unexpected) — nothing sensible to append after
            }

            var last = academicsItems.OrderByDescending(m => m.SortOrder).First();

            context.MenuItems.Add(new MenuItem
            {
                Portal = "admin",
                Section = "Academics",
                SectionOrder = last.SectionOrder,
                Label = "Quiz Bank",
                Path = path,
                Icon = "Sparkles",
                SortOrder = last.SortOrder + 1,
                IsActive = true,
                RequiredModule = PermissionModule.CourseBatchManagement,
            });
        }

        /// <summary>
        /// Inserts the Admin "Activity Bank" menu item into a database that was seeded before
        /// the admin-authored whiteboard activity bank existed (SeedMenusAsync only ever
        /// creates rows once). Appended after whatever the Academics section's last item
        /// currently is, same placement rule as EnsureAdminQuizBankMenuAsync right above.
        /// </summary>
        private static async Task EnsureAdminActivityBankMenuAsync(ReaderNestDbContext context)
        {
            const string path = "/admin/activity-bank";
            if (context.MenuItems.Local.Any(m => m.Portal == "admin" && m.Path == path) ||
                await context.MenuItems.AnyAsync(m => m.Portal == "admin" && m.Path == path))
            {
                return;
            }

            var academicsItems = await context.MenuItems
                .Where(m => m.Portal == "admin" && m.Section == "Academics")
                .ToListAsync();
            if (academicsItems.Count == 0)
            {
                return; // no Academics section at all (unexpected) — nothing sensible to append after
            }

            var last = academicsItems.OrderByDescending(m => m.SortOrder).First();

            context.MenuItems.Add(new MenuItem
            {
                Portal = "admin",
                Section = "Academics",
                SectionOrder = last.SectionOrder,
                Label = "Activity Bank",
                Path = path,
                Icon = "PencilRuler",
                SortOrder = last.SortOrder + 1,
                IsActive = true,
                RequiredModule = PermissionModule.CourseBatchManagement,
            });
        }

        /// <summary>
        /// Inserts the Admin "Server Monitoring" menu item into a database that was seeded
        /// before that screen existed (SeedMenusAsync only ever creates rows once). Appended
        /// after whatever the System section's last item currently is (today just "Settings &amp;
        /// Branding"), same idiom as EnsureAdminQuizBankMenuAsync.
        /// </summary>
        private static async Task EnsureAdminServerMonitoringMenuAsync(ReaderNestDbContext context)
        {
            const string path = "/admin/monitoring";
            if (context.MenuItems.Local.Any(m => m.Portal == "admin" && m.Path == path) ||
                await context.MenuItems.AnyAsync(m => m.Portal == "admin" && m.Path == path))
            {
                return;
            }

            var systemItems = await context.MenuItems
                .Where(m => m.Portal == "admin" && m.Section == "System")
                .ToListAsync();
            if (systemItems.Count == 0)
            {
                return; // no System section at all (unexpected) — nothing sensible to append after
            }

            var last = systemItems.OrderByDescending(m => m.SortOrder).First();

            context.MenuItems.Add(new MenuItem
            {
                Portal = "admin",
                Section = "System",
                SectionOrder = last.SectionOrder,
                Label = "Server Monitoring",
                Path = path,
                Icon = "Activity",
                SortOrder = last.SortOrder + 1,
                IsActive = true,
                RequiredModule = PermissionModule.SystemMonitoring,
            });
        }

        private static async Task SeedMenusAsync(ReaderNestDbContext context)
        {
            if (await context.MenuItems.AnyAsync())
            {
                return;
            }

            var items = MenuSeedItems();
            var sectionOrders = new Dictionary<string, int>();
            var sortOrders = new Dictionary<string, int>();
            foreach (var (portal, section, label, path, icon, requiredModule) in items)
            {
                var sectionKey = $"{portal}|{section}";
                if (!sectionOrders.TryGetValue(sectionKey, out var sectionOrder))
                {
                    sectionOrder = sectionOrders.Count(kv => kv.Key.StartsWith($"{portal}|", StringComparison.Ordinal));
                    sectionOrders[sectionKey] = sectionOrder;
                }

                var sortOrder = sortOrders.TryGetValue(sectionKey, out var current) ? current : 0;
                sortOrders[sectionKey] = sortOrder + 1;

                context.MenuItems.Add(new MenuItem
                {
                    Portal = portal,
                    Section = section,
                    SectionOrder = sectionOrder,
                    Label = label,
                    Path = path,
                    Icon = icon,
                    SortOrder = sortOrder,
                    RequiredModule = requiredModule,
                    IsActive = true,
                });
            }
        }

        private static async Task SeedIntegrationsAsync(ReaderNestDbContext context)
        {
            if (await context.Integrations.AnyAsync())
            {
                return;
            }

            string Json(Dictionary<string, string?> config) => JsonSerializer.Serialize(config);

            context.Integrations.AddRange(
                new Integration
                {
                    Key = "email",
                    Name = "Email (SMTP)",
                    Category = IntegrationCategory.Email,
                    Description = "Transactional email for confirmations, reminders and reports.",
                    IsEnabled = true,
                    IsSystem = true,
                    ConfigJson = Json(new() { ["fromAddress"] = "support@meettomanage.cloud", ["smtpHost"] = "", ["smtpPort"] = "587" }),
                },
                new Integration
                {
                    Key = "whatsapp",
                    Name = "WhatsApp Business API",
                    Category = IntegrationCategory.Messaging,
                    Description = "Parent communication and reminders over WhatsApp.",
                    IsEnabled = false,
                    IsSystem = true,
                    ConfigJson = Json(new() { ["phoneNumberId"] = "", ["accessToken"] = "" }),
                },
                new Integration
                {
                    Key = "razorpay",
                    Name = "Razorpay",
                    Category = IntegrationCategory.PaymentGateway,
                    Description = "Payment gateway — Phonics department.",
                    IsEnabled = true,
                    IsSystem = true,
                    ConfigJson = Json(new() { ["keyId"] = "", ["keySecret"] = "", ["webhookSecret"] = "" }),
                },
                new Integration
                {
                    Key = "cashfree",
                    Name = "Cashfree",
                    Category = IntegrationCategory.PaymentGateway,
                    Description = "Payment gateway — Maths department.",
                    IsEnabled = true,
                    IsSystem = true,
                    ConfigJson = Json(new() { ["appId"] = "", ["secretKey"] = "" }),
                },
                new Integration
                {
                    Key = "zoom",
                    Name = "Zoom",
                    Category = IntegrationCategory.VideoConferencing,
                    Description = "Alternate live classroom video conferencing.",
                    IsEnabled = false,
                    IsSystem = true,
                    ConfigJson = Json(new() { ["apiKey"] = "", ["apiSecret"] = "" }),
                },
                new Integration
                {
                    Key = "jitsi",
                    Name = "Jitsi Meet",
                    Category = IntegrationCategory.VideoConferencing,
                    Description = "Primary live classroom video conferencing (self-hosted).",
                    IsEnabled = true,
                    IsSystem = true,
                    // appId/appSecret are optional: blank means every join is unsigned, exactly
                    // today's behaviour. Set both (and turn on prosody token_verification on the
                    // Jitsi deployment — see docs/JITSI_ARCHITECTURE.md) to require a valid,
                    // room-scoped token to join. autoRecord defaults on to match current behaviour.
                    ConfigJson = Json(new() { ["domain"] = "meet.techmisai.com", ["appId"] = "", ["appSecret"] = "", ["autoRecord"] = "true" }),
                },
                CashPaymentMethod());
        }

        /// <summary>
        /// Cash is a first-class payment method managed like a gateway in Settings → Integrations,
        /// so it shows in the parent Pay-Now popup only while enabled. Runs every startup (insert-if-absent)
        /// so it also lands in databases that were seeded before Cash existed.
        /// </summary>
        private static async Task EnsureCashPaymentMethodAsync(ReaderNestDbContext context)
        {
            // On a fresh database SeedIntegrationsAsync has already queued this row in the
            // change tracker but nothing is saved until the single SaveChangesAsync at the
            // end, so a database-only existence check would insert "cash" twice (23505 on
            // ix_integrations_key). Check pending local entities first.
            if (context.Integrations.Local.Any(i => i.Key == "cash") ||
                await context.Integrations.AnyAsync(i => i.Key == "cash"))
            {
                return;
            }

            context.Integrations.Add(CashPaymentMethod());
        }

        private static Integration CashPaymentMethod() => new()
        {
            Key = "cash",
            Name = "Cash",
            Category = IntegrationCategory.PaymentGateway,
            Description = "Offline cash payment collected at the centre.",
            IsEnabled = true,
            IsSystem = true,
            ConfigJson = "{}",
        };

        /// <summary>
        /// SMS reminders/credentials channel (MSG91 or Twilio). Insert-if-absent every
        /// startup so it also lands in databases seeded before SMS support existed.
        /// </summary>
        private static async Task EnsureSmsIntegrationAsync(ReaderNestDbContext context)
        {
            // Local check mirrors EnsureCashPaymentMethodAsync: never trust the database
            // alone while unsaved seed rows are still sitting in the change tracker.
            if (context.Integrations.Local.Any(i => i.Key == "sms") ||
                await context.Integrations.AnyAsync(i => i.Key == "sms"))
            {
                return;
            }

            context.Integrations.Add(new Integration
            {
                Key = "sms",
                Name = "SMS",
                Category = IntegrationCategory.Messaging,
                Description = "Transactional SMS for reminders and onboarding credentials (provider: msg91 or twilio).",
                IsEnabled = false,
                IsSystem = true,
                ConfigJson = JsonSerializer.Serialize(new Dictionary<string, string?>
                {
                    ["provider"] = "msg91",
                    ["authKey"] = "",
                    ["senderId"] = "",
                    ["accountSid"] = "",
                    ["authToken"] = "",
                    ["fromNumber"] = "",
                }),
            });
        }

        /// <summary>
        /// Backfills the "autoRecord" key into an already-seeded "jitsi" Integration's ConfigJson
        /// (databases created before this toggle existed). Runs every startup, no-ops once the
        /// key is present so it never clobbers an admin's own on/off choice made in Settings.
        /// </summary>
        private static async Task EnsureJitsiAutoRecordConfigAsync(ReaderNestDbContext context)
        {
            var jitsi = context.Integrations.Local.FirstOrDefault(i => i.Key == "jitsi")
                ?? await context.Integrations.FirstOrDefaultAsync(i => i.Key == "jitsi");
            if (jitsi is null)
            {
                return;
            }

            var config = string.IsNullOrWhiteSpace(jitsi.ConfigJson)
                ? new Dictionary<string, string?>()
                : JsonSerializer.Deserialize<Dictionary<string, string?>>(jitsi.ConfigJson) ?? new Dictionary<string, string?>();

            if (config.ContainsKey("autoRecord"))
            {
                return;
            }

            config["autoRecord"] = "true";
            jitsi.ConfigJson = JsonSerializer.Serialize(config);
        }

        /// <summary>
        /// Retrofits the "Email Templates" admin menu item into a database that was seeded
        /// before it existed (mirrors EnsureSubAdminIntegrationsMenuAsync). Fresh databases
        /// already get it from MenuSeedItems(); this only fires for pre-existing ones.
        /// </summary>
        private static async Task EnsureEmailTemplatesMenuAsync(ReaderNestDbContext context)
        {
            if (context.MenuItems.Local.Any(m => m.Portal == "admin" && m.Path == "/admin/email-templates") ||
                await context.MenuItems.AnyAsync(m => m.Portal == "admin" && m.Path == "/admin/email-templates"))
            {
                return;
            }

            var bulkEmail = await context.MenuItems
                .FirstOrDefaultAsync(m => m.Portal == "admin" && m.Path == "/admin/bulk-email");

            context.MenuItems.Add(new MenuItem
            {
                Portal = "admin",
                Section = "Insights",
                SectionOrder = bulkEmail?.SectionOrder ?? 4,
                Label = "Email Templates",
                Path = "/admin/email-templates",
                Icon = "FileText",
                SortOrder = (bulkEmail?.SortOrder ?? 0) + 1,
                IsActive = true,
                RequiredModule = PermissionModule.Communication,
            });
        }

        /// <summary>
        /// Retrofits the "Bulk Email History" admin menu item into a database that was seeded
        /// before it existed (mirrors EnsureEmailTemplatesMenuAsync). Fresh databases already
        /// get it from MenuSeedItems(); this only fires for pre-existing ones.
        /// </summary>
        private static async Task EnsureBulkEmailHistoryMenuAsync(ReaderNestDbContext context)
        {
            if (context.MenuItems.Local.Any(m => m.Portal == "admin" && m.Path == "/admin/bulk-email/history") ||
                await context.MenuItems.AnyAsync(m => m.Portal == "admin" && m.Path == "/admin/bulk-email/history"))
            {
                return;
            }

            var bulkEmail = await context.MenuItems
                .FirstOrDefaultAsync(m => m.Portal == "admin" && m.Path == "/admin/bulk-email");

            context.MenuItems.Add(new MenuItem
            {
                Portal = "admin",
                Section = "Insights",
                SectionOrder = bulkEmail?.SectionOrder ?? 4,
                Label = "Bulk Email History",
                Path = "/admin/bulk-email/history",
                Icon = "History",
                SortOrder = (bulkEmail?.SortOrder ?? 0) + 1,
                IsActive = true,
                RequiredModule = PermissionModule.Communication,
            });
        }

        /// <summary>
        /// Retrofits the "Progress Reports" admin menu item into a database that was seeded
        /// before it existed (mirrors EnsureEmailTemplatesMenuAsync). Fresh databases already
        /// get it from MenuSeedItems(); this only fires for pre-existing ones.
        /// </summary>
        private static async Task EnsureProgressReportsMenuAsync(ReaderNestDbContext context)
        {
            if (context.MenuItems.Local.Any(m => m.Portal == "admin" && m.Path == "/admin/progress-reports") ||
                await context.MenuItems.AnyAsync(m => m.Portal == "admin" && m.Path == "/admin/progress-reports"))
            {
                return;
            }

            var emailTemplates = await context.MenuItems
                .FirstOrDefaultAsync(m => m.Portal == "admin" && m.Path == "/admin/email-templates");

            context.MenuItems.Add(new MenuItem
            {
                Portal = "admin",
                Section = "Insights",
                SectionOrder = emailTemplates?.SectionOrder ?? 4,
                Label = "Progress Reports",
                Path = "/admin/progress-reports",
                Icon = "ScrollText",
                SortOrder = (emailTemplates?.SortOrder ?? 0) + 1,
                IsActive = true,
                RequiredModule = PermissionModule.Communication,
            });
        }

        /// <summary>
        /// Retrofits the Admin "Doubt Chatbot" and Teacher "Student Doubts" menu items into a
        /// database seeded before the chatbot feature existed (mirrors EnsureProgressReportsMenuAsync).
        /// Fresh databases already get both from MenuSeedItems().
        /// </summary>
        private static async Task EnsureChatbotMenusAsync(ReaderNestDbContext context)
        {
            if (!context.MenuItems.Local.Any(m => m.Portal == "admin" && m.Path == "/admin/chatbot") &&
                !await context.MenuItems.AnyAsync(m => m.Portal == "admin" && m.Path == "/admin/chatbot"))
            {
                var progressReports = await context.MenuItems
                    .FirstOrDefaultAsync(m => m.Portal == "admin" && m.Path == "/admin/progress-reports");

                context.MenuItems.Add(new MenuItem
                {
                    Portal = "admin",
                    Section = "Insights",
                    SectionOrder = progressReports?.SectionOrder ?? 4,
                    Label = "Doubt Chatbot",
                    Path = "/admin/chatbot",
                    Icon = "MessageCircleQuestion",
                    SortOrder = (progressReports?.SortOrder ?? 0) + 1,
                    IsActive = true,
                    RequiredModule = PermissionModule.Communication,
                });
            }

            if (!context.MenuItems.Local.Any(m => m.Portal == "teacher" && m.Path == "/teacher/doubts") &&
                !await context.MenuItems.AnyAsync(m => m.Portal == "teacher" && m.Path == "/teacher/doubts"))
            {
                var demoFeedback = await context.MenuItems
                    .FirstOrDefaultAsync(m => m.Portal == "teacher" && m.Path == "/teacher/demo-feedback");

                context.MenuItems.Add(new MenuItem
                {
                    Portal = "teacher",
                    Section = "Teaching",
                    SectionOrder = demoFeedback?.SectionOrder ?? 0,
                    Label = "Student Doubts",
                    Path = "/teacher/doubts",
                    Icon = "MessageCircleQuestion",
                    SortOrder = (demoFeedback?.SortOrder ?? 0) + 1,
                    IsActive = true,
                    RequiredModule = PermissionModule.Communication,
                });
            }
        }

        /// <summary>
        /// Starter FAQ knowledge base for the "Ask a Doubt" chatbot — without this, a fresh
        /// database has zero FAQs and every question (including a plain "hi") falls through
        /// to teacher escalation, which is technically correct but useless out of the box.
        /// Only runs once: skipped entirely once any ChatFaq row exists, so an admin's own
        /// edits/deletes here are never re-added or overwritten on the next startup.
        /// </summary>
        private static async Task SeedChatFaqsAsync(ReaderNestDbContext context)
        {
            if (context.ChatFaqs.Local.Count > 0 || await context.ChatFaqs.AnyAsync())
            {
                return;
            }

            (string Question, string Answer, string Keywords, string Category)[] seeds =
            [
                ("How do I join my live class?",
                 "Go to Schedule & Live Class (or My Classes if you're a teacher) and tap \"Join\" once the session shows as live — it opens a few minutes before the scheduled start time.",
                 "join, live, class, session, meeting, link", "Classes"),
                ("How do I schedule a demo class?",
                 "Demo scheduling is handled by our Admission team — reach out via the contact details on your enrollment confirmation, or ask here and a teacher will follow up to arrange a time.",
                 "schedule, demo, trial, book, appointment", "Classes"),
                ("How do I pay my fees?",
                 "Open Payments & Billing from your portal menu, pick the invoice, and pay by card, UPI, or bank transfer. You'll get a receipt by email once it clears.",
                 "pay, fees, fee, billing, invoice, payment, money", "Billing"),
                ("I forgot my password, what do I do?",
                 "Use \"Forgot password\" on the login screen to reset it by email. If you don't get the email within a few minutes, check your spam folder or contact your coordinator.",
                 "forgot, password, pin, login, reset, locked, access", "Account"),
                ("Where can I find recordings of past classes?",
                 "Recordings live under Recordings in your portal menu, listed by course and date, usually available within a couple of hours after the class ends.",
                 "recording, recordings, video, past, missed, replay", "Classes"),
                ("How do I check attendance?",
                 "Attendance & Records shows every session's status. Teachers mark it right after class; it usually reflects within a few minutes.",
                 "attendance, present, absent, records", "Classes"),
                ("How do I contact my teacher?",
                 "Use Notifications & Reports to message through the platform, or ask during your next live class — teachers don't share personal contact details directly.",
                 "contact, teacher, message, talk, reach", "Communication"),
                ("Where do I get homework or study resources?",
                 "Check Resources in your portal menu — teachers upload homework, worksheets and study material there, organized by course.",
                 "homework, resources, worksheet, study, material, assignment", "Classes"),
            ];

            for (var i = 0; i < seeds.Length; i++)
            {
                var (question, answer, keywords, category) = seeds[i];
                context.ChatFaqs.Add(new ChatFaq
                {
                    Question = question,
                    Answer = answer,
                    Keywords = keywords,
                    Category = category,
                    IsActive = true,
                    SortOrder = i,
                });
            }
        }

        /// <summary>
        /// Widens the chatbot's free, rule-based coverage with more common doubts, added after
        /// the original starter set shipped. SeedChatFaqsAsync only ever runs once (it bails
        /// the instant any ChatFaq row exists), so new starter entries need their own
        /// idempotent, per-question backfill here instead of just growing that seed array.
        /// </summary>
        private static async Task EnsureAdditionalChatFaqsAsync(ReaderNestDbContext context)
        {
            (string Question, string Answer, string Keywords, string Category)[] additions =
            [
                ("My audio or video isn't working during class",
                 "Refresh the page and rejoin first — that fixes it most of the time. Otherwise check your browser has given the site camera/microphone permission, and that no other app (Zoom, another tab) is already using your camera or mic.",
                 "audio, video, mic, microphone, camera, sound, hear, see, not working", "Classes"),
                ("How do I use the whiteboard in class?",
                 "The whiteboard opens automatically inside the live classroom. Your teacher controls who can draw — if you can't, ask them to give you board access during the session.",
                 "whiteboard, draw, board, write", "Classes"),
                ("How does the quiz during class work?",
                 "When a teacher launches a live quiz, it pops up automatically in your classroom window — just pick your answer before time runs out. There's nothing to open separately.",
                 "quiz, test, question, live quiz", "Classes"),
                ("Can I get a refund if I cancel?",
                 "Refund and cancellation requests are reviewed case-by-case — raise it here or with Admission and a teacher/admin will get back to you with the details for your enrollment.",
                 "refund, cancel, cancellation, money back", "Billing"),
                ("How do I add another child to my account?",
                 "Use Add Child from your parent portal menu to enroll a sibling under the same account — you'll see both children from the same login afterward.",
                 "add child, sibling, another child, second child, enroll", "Account"),
                ("How do I request leave as a teacher?",
                 "Use Leave Management in your portal menu to submit a request with your dates — your coordinator gets notified and approves or declines it there.",
                 "leave, time off, absence, sick, vacation", "Account"),
                ("When will I get my payout?",
                 "Check My Payout in your portal menu for the schedule and status of your upcoming payout — it's calculated from your completed, attendance-confirmed classes.",
                 "payout, salary, payment, earn, earnings, paid", "Billing"),
                ("What if my internet disconnects during class?",
                 "Just rejoin the same class link as soon as you're back online — the session keeps running, and you'll rejoin right where it is. Recordings are also available afterward if you miss too much.",
                 "internet, disconnect, connection, dropped, lost, reconnect", "Classes"),
            ];

            var existingQuestions = (context.ChatFaqs.Local.Count > 0 ? context.ChatFaqs.Local.AsEnumerable() : [])
                .Concat(await context.ChatFaqs.ToListAsync())
                .Select(f => f.Question)
                .ToHashSet();

            var nextSortOrder = (context.ChatFaqs.Local.Count > 0 ? context.ChatFaqs.Local.Max(f => (int?)f.SortOrder) : null)
                ?? await context.ChatFaqs.MaxAsync(f => (int?)f.SortOrder)
                ?? -1;
            nextSortOrder++;

            foreach (var (question, answer, keywords, category) in additions)
            {
                if (existingQuestions.Contains(question))
                {
                    continue;
                }

                context.ChatFaqs.Add(new ChatFaq
                {
                    Question = question,
                    Answer = answer,
                    Keywords = keywords,
                    Category = category,
                    IsActive = true,
                    SortOrder = nextSortOrder++,
                });
            }
        }

        /// <summary>
        /// Retrofits the "Store Inquiries" admin menu item into a database that was seeded
        /// before it existed (mirrors EnsureProgressReportsMenuAsync). Fresh databases already
        /// get it from MenuSeedItems(); this only fires for pre-existing ones.
        /// </summary>
        private static async Task EnsureStoreInquiriesMenuAsync(ReaderNestDbContext context)
        {
            if (context.MenuItems.Local.Any(m => m.Portal == "admin" && m.Path == "/admin/store-inquiries") ||
                await context.MenuItems.AnyAsync(m => m.Portal == "admin" && m.Path == "/admin/store-inquiries"))
            {
                return;
            }

            var enrollmentReview = await context.MenuItems
                .FirstOrDefaultAsync(m => m.Portal == "admin" && m.Path == "/admin/enrollments");

            context.MenuItems.Add(new MenuItem
            {
                Portal = "admin",
                Section = "People",
                SectionOrder = enrollmentReview?.SectionOrder ?? 2,
                Label = "Store Inquiries",
                Path = "/admin/store-inquiries",
                Icon = "ShoppingBag",
                SortOrder = (enrollmentReview?.SortOrder ?? 0) + 1,
                IsActive = true,
                RequiredModule = PermissionModule.Admission,
            });
        }

        /// <summary>
        /// Retrofits the "Recordings" parent menu item (mirrors EnsureProgressReportsMenuAsync).
        /// Fresh databases already get it from MenuSeedItems(); a pre-existing one only had
        /// "Resources &amp; Recordings" → /parent/resources, whose own Recordings tab actually
        /// reads a different, unrelated data source (the generic Resource library, not
        /// SessionRecording) — so this also renames that older row back to plain "Resources"
        /// (only if it still has its original label, so a since-hand-edited one is left alone)
        /// to stop the two screens claiming the same name for two different things.
        /// </summary>
        private static async Task EnsureParentRecordingsMenuAsync(ReaderNestDbContext context)
        {
            var resources = await context.MenuItems
                .FirstOrDefaultAsync(m => m.Portal == "parent" && m.Path == "/parent/resources");
            if (resources is not null && resources.Label == "Resources & Recordings")
            {
                resources.Label = "Resources";
            }

            if (context.MenuItems.Local.Any(m => m.Portal == "parent" && m.Path == "/parent/recordings") ||
                await context.MenuItems.AnyAsync(m => m.Portal == "parent" && m.Path == "/parent/recordings"))
            {
                return;
            }

            context.MenuItems.Add(new MenuItem
            {
                Portal = "parent",
                Section = "Learning",
                SectionOrder = resources?.SectionOrder ?? 0,
                Label = "Recordings",
                Path = "/parent/recordings",
                Icon = "Video",
                SortOrder = (resources?.SortOrder ?? 0) + 1,
                IsActive = true,
                RequiredModule = PermissionModule.ContentAccessManagement,
            });
        }

        /// <summary>
        /// SeedEmailTemplatesAsync is insert-only, so a live DB that predates this template
        /// never picks it up on its own — inserts just the "progress-report" row if missing.
        /// </summary>
        private static async Task EnsureProgressReportEmailTemplateAsync(ReaderNestDbContext context)
        {
            if (context.EmailTemplates.Local.Any(t => t.Key == "progress-report") ||
                await context.EmailTemplates.AnyAsync(t => t.Key == "progress-report"))
            {
                return;
            }

            var seed = EmailTemplateSeedData.All.First(s => s.Key == "progress-report");
            context.EmailTemplates.Add(new EmailTemplate
            {
                Key = seed.Key,
                Name = seed.Name,
                Description = seed.Description,
                Category = seed.Category,
                Subject = seed.Subject,
                HtmlBody = seed.HtmlBody,
                PlaceholdersJson = JsonSerializer.Serialize(seed.Placeholders),
                IsActive = true,
                IsSystem = true,
            });
        }

        /// <summary>
        /// SeedEmailTemplatesAsync is insert-only, so a live DB that predates the self-service
        /// PIN reset feature never picks it up on its own — inserts just the "pin-reset" row
        /// if missing, mirroring EnsureProgressReportEmailTemplateAsync.
        /// </summary>
        private static async Task EnsurePinResetEmailTemplateAsync(ReaderNestDbContext context)
        {
            if (context.EmailTemplates.Local.Any(t => t.Key == "pin-reset") ||
                await context.EmailTemplates.AnyAsync(t => t.Key == "pin-reset"))
            {
                return;
            }

            var seed = EmailTemplateSeedData.All.First(s => s.Key == "pin-reset");
            context.EmailTemplates.Add(new EmailTemplate
            {
                Key = seed.Key,
                Name = seed.Name,
                Description = seed.Description,
                Category = seed.Category,
                Subject = seed.Subject,
                HtmlBody = seed.HtmlBody,
                PlaceholdersJson = JsonSerializer.Serialize(seed.Placeholders),
                IsActive = true,
                IsSystem = true,
            });
        }

        /// <summary>
        /// Email Template Master: every automated system email's Subject/HtmlBody, built from
        /// the shared <see cref="EmailTemplateSeedData"/> catalog (also used by tests, so both
        /// exercise identical rendered content). Insert-only (skips entirely once any row
        /// exists) so an admin's edits are never overwritten by a later deploy.
        /// </summary>
        private static async Task SeedEmailTemplatesAsync(ReaderNestDbContext context)
        {
            if (await context.EmailTemplates.AnyAsync())
            {
                return;
            }

            context.EmailTemplates.AddRange(EmailTemplateSeedData.All.Select(seed => new EmailTemplate
            {
                Key = seed.Key,
                Name = seed.Name,
                Description = seed.Description,
                Category = seed.Category,
                Subject = seed.Subject,
                HtmlBody = seed.HtmlBody,
                PlaceholdersJson = JsonSerializer.Serialize(seed.Placeholders),
                IsActive = true,
                IsSystem = true,
            }));
        }

        /// <summary>
        /// SeedEmailTemplatesAsync is insert-only, so a live DB that predates the Sub Admin
        /// "request additional access" feature never picks these up on its own — inserts
        /// whichever of the two rows is missing, mirroring EnsurePinResetEmailTemplateAsync.
        /// </summary>
        private static async Task EnsureAccessRequestEmailTemplatesAsync(ReaderNestDbContext context)
        {
            foreach (var key in new[] { "access-request-submitted-admin-alert", "access-request-reviewed" })
            {
                if (context.EmailTemplates.Local.Any(t => t.Key == key) ||
                    await context.EmailTemplates.AnyAsync(t => t.Key == key))
                {
                    continue;
                }

                var seed = EmailTemplateSeedData.All.First(s => s.Key == key);
                context.EmailTemplates.Add(new EmailTemplate
                {
                    Key = seed.Key,
                    Name = seed.Name,
                    Description = seed.Description,
                    Category = seed.Category,
                    Subject = seed.Subject,
                    HtmlBody = seed.HtmlBody,
                    PlaceholdersJson = JsonSerializer.Serialize(seed.Placeholders),
                    IsActive = true,
                    IsSystem = true,
                });
            }
        }

        /// <summary>
        /// SeedEmailTemplatesAsync is insert-only, so a live DB never picks up template text
        /// changes on its own — this backfills the direct {{JoinUrl}} join-link button into
        /// the two templates that gained it, skipping any row that already has the token
        /// (idempotent, and leaves an admin's own subsequent edits alone).
        /// </summary>
        private static async Task ReconcileJoinLinkEmailTemplatesAsync(ReaderNestDbContext context)
        {
            foreach (var seed in EmailTemplateSeedData.All.Where(s => s.Key is "demo-confirmed" or "session-reminder-parent"))
            {
                var existing = await context.EmailTemplates.FirstOrDefaultAsync(t => t.Key == seed.Key);
                if (existing is null || existing.HtmlBody.Contains("{{JoinUrl}}"))
                {
                    continue;
                }

                existing.Subject = seed.Subject;
                existing.HtmlBody = seed.HtmlBody;
                existing.PlaceholdersJson = JsonSerializer.Serialize(seed.Placeholders);
            }
        }

        /// <summary>
        /// Notification.Body used to store the full rendered HTML email verbatim; the in-app
        /// bell/feed rendered that raw markup as text (visible as literal "&lt;div style=..."
        /// in the UI). NotificationService now strips it to plain text at write time via
        /// <see cref="HtmlText.PlainTextFromHtml"/>, but that only fixed notifications created
        /// after the change — rows written earlier still hold raw HTML. Runs every startup,
        /// scoped to rows that still look like markup, so it's cheap once the backlog is clean
        /// and never touches a row that's already plain text (including one that coincidentally
        /// used "&lt;"/"&gt;" — re-stripping plain text is a no-op).
        /// </summary>
        private static async Task BackfillPlainTextNotificationBodiesAsync(ReaderNestDbContext context)
        {
            var stale = await context.Notifications
                .Where(n => n.Body.Contains("<") && n.Body.Contains(">"))
                .ToListAsync();

            foreach (var notification in stale)
            {
                notification.Body = HtmlText.PlainTextFromHtml(notification.Body);
            }
        }

        /// <summary>
        /// SeedEmailTemplatesAsync is insert-only, so a live DB seeded before the PIN-login
        /// switch still has "welcome-credentials" rendering {{TemporaryPassword}} — a token
        /// UserService no longer supplies (it now sends TemporaryPin), so that row would
        /// render blank. Reconciles it to the new copy/token, unless it's already been
        /// updated (or an admin edited it since — same accepted trade-off as
        /// ReconcileJoinLinkEmailTemplatesAsync above).
        /// </summary>
        private static async Task ReconcileWelcomeCredentialsPinTemplateAsync(ReaderNestDbContext context)
        {
            var seed = EmailTemplateSeedData.All.First(s => s.Key == "welcome-credentials");
            var existing = await context.EmailTemplates.FirstOrDefaultAsync(t => t.Key == seed.Key);
            if (existing is null || existing.HtmlBody.Contains("{{TemporaryPin}}"))
            {
                return;
            }

            existing.Subject = seed.Subject;
            existing.HtmlBody = seed.HtmlBody;
            existing.PlaceholdersJson = JsonSerializer.Serialize(seed.Placeholders);
        }

        /// <summary>
        /// SeedEmailTemplatesAsync is insert-only, so a live DB seeded before every template's
        /// header/footer (and two subjects/bodies) switched from a hardcoded "The Reader Nest"
        /// to {{OrgName}} — resolved live from Settings -> Branding by EmailTemplateService,
        /// see docs/WHITE_LABEL_BRANDING.md's "Product naming" row — still has the old fixed
        /// text baked in. Reconciles every template still missing the token, skipping any an
        /// admin has already edited themselves (same trade-off as the other Reconcile* methods
        /// above: an admin's own subsequent edit is left alone, not overwritten again later).
        /// </summary>
        private static async Task ReconcileOrgNameEmailTemplatesAsync(ReaderNestDbContext context)
        {
            foreach (var seed in EmailTemplateSeedData.All)
            {
                var existing = await context.EmailTemplates.FirstOrDefaultAsync(t => t.Key == seed.Key);
                if (existing is null || existing.HtmlBody.Contains("{{OrgName}}"))
                {
                    continue;
                }

                existing.Subject = seed.Subject;
                existing.HtmlBody = seed.HtmlBody;
                existing.PlaceholdersJson = JsonSerializer.Serialize(seed.Placeholders);
            }
        }
    }
}
