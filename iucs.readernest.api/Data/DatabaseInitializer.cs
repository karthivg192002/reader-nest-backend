using System.Text.Json;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Dto.Users;
using iucs.readernest.domain.Data;
using iucs.readernest.domain.Entities.Billing;
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
            await SeedPaymentAccountsAsync(context);
            await SeedSettingsAsync(context);
            await SeedRolesAsync(context);
            await BackfillSystemRolePermissionsAsync(context);
            await SeedMenusAsync(context);
            await RemoveRetiredMenusAsync(context);
            await EnsureSubAdminIntegrationsMenuAsync(context);
            await EnsurePackagesAndStudentViewMenusAsync(context);
            await BackfillMenuRequiredModulesAsync(context);
            await SeedIntegrationsAsync(context);
            await EnsureCashPaymentMethodAsync(context);
            await EnsureSmsIntegrationAsync(context);

            await context.SaveChangesAsync();
        }

        private static async Task SeedAdminAsync(
            IServiceProvider services,
            ReaderNestDbContext context,
            IConfiguration configuration)
        {
            var email = configuration["Seed:AdminEmail"];
            var password = configuration["Seed:AdminPassword"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
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
                PasswordHash = hasher.Hash(password),
                FirstName = configuration["Seed:AdminFirstName"] ?? "Reader Nest",
                LastName = configuration["Seed:AdminLastName"] ?? "Admin",
                Role = UserRole.Admin,
            });
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
                    Department = Department.Phonics,
                    GatewayProvider = "razorpay",
                    GatewayAccountRef = "phonics-account",
                },
                new PaymentAccount
                {
                    Name = "Maths Department Account",
                    Department = Department.Maths,
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
                Setting(SettingCategory.General, "org.name", "The Reader Nest", isPublic: true),
                Setting(SettingCategory.General, "org.domain", "app.thereadernest.com"),
                Setting(SettingCategory.General, "org.supportEmail", "support@thereadernest.com"),
                Setting(SettingCategory.General, "org.supportPhone", "+91 98200 00000"),
                Setting(SettingCategory.General, "org.timezone", "Asia/Kolkata (GMT +5:30)"),
                Setting(SettingCategory.Branding, "brand.name", "The Reader Nest", isPublic: true),
                Setting(SettingCategory.Branding, "brand.logoUrl", null, isPublic: true),
                Setting(SettingCategory.Branding, "brand.primaryColor", "#1F6FE0", isPublic: true),
                Setting(SettingCategory.Branding, "brand.accentColor", "#57B33B", isPublic: true),
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
            (string RoleName, PermissionModule Module, bool View, bool Create, bool Edit, bool Delete, bool Approve)[] additions =
            [
                ("teacher", PermissionModule.Payouts, true, false, false, false, false),
                ("parent", PermissionModule.SessionCalendarManagement, true, false, false, false, false),
                ("parent", PermissionModule.ContentAccessManagement, true, false, false, false, false),
                ("parent", PermissionModule.BillingFinance, true, false, false, false, false),
                ("parent", PermissionModule.Communication, true, false, false, false, false),
                ("admission", PermissionModule.BillingFinance, true, false, true, false, true),
            ];

            var roleNames = additions.Select(a => a.RoleName).Distinct().ToList();
            var roles = await context.RoleDefinitions
                .Include(r => r.Permissions)
                .Where(r => roleNames.Contains(r.Name))
                .ToListAsync();

            foreach (var (roleName, module, view, create, edit, delete, approve) in additions)
            {
                var role = roles.FirstOrDefault(r => r.Name == roleName);
                if (role is null || role.Permissions.Any(p => p.Module == module))
                {
                    continue;
                }

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
            ("admin", "Academics", "Batches", "/admin/batches", "Layers", PermissionModule.CourseBatchManagement),
            ("admin", "Academics", "Academic Calendar", "/admin/calendar", "CalendarDays", PermissionModule.SessionCalendarManagement),
            ("admin", "Academics", "Sessions", "/admin/sessions", "CalendarClock", PermissionModule.SessionCalendarManagement),
            ("admin", "People", "Users", "/admin/users", "Users", PermissionModule.UserManagement),
            ("admin", "People", "Roles & Permissions", "/admin/permissions", "ShieldCheck", PermissionModule.UserManagement),
            ("admin", "People", "Enrollment Review", "/admin/enrollments", "ClipboardCheck", PermissionModule.Admission),
            ("admin", "Content", "Content & Resources", "/admin/resources", "FolderOpen", PermissionModule.ContentAccessManagement),
            ("admin", "Finance", "Billing & Finance", "/admin/billing", "Receipt", PermissionModule.BillingFinance),
            ("admin", "Finance", "Packages & Subscriptions", "/admin/packages", "CreditCard", PermissionModule.BillingFinance),
            ("admin", "Finance", "Payment Gateway Mapping", "/admin/payment-mapping", "Landmark", PermissionModule.BillingFinance),
            ("admin", "Finance", "Teacher Payouts", "/admin/payouts", "Wallet", PermissionModule.Payouts),
            ("admin", "Finance", "Fee Suspension", "/admin/fee-suspension", "Ban", PermissionModule.BillingFinance),
            ("admin", "Insights", "Reports & Analytics", "/admin/reports", "BarChart3", PermissionModule.ReportsAnalytics),
            ("admin", "Insights", "Bulk Email", "/admin/bulk-email", "Mail", PermissionModule.Communication),
            ("admin", "System", "Settings & Branding", "/admin/settings", "Settings", PermissionModule.Settings),
            ("teacher", null, "Dashboard", "/teacher", "LayoutDashboard", null),
            ("teacher", "Teaching", "My Classes", "/teacher/classes", "CalendarClock", PermissionModule.SessionCalendarManagement),
            ("teacher", "Teaching", "Live Classroom", "/teacher/live/s-1", "Video", PermissionModule.SessionCalendarManagement),
            ("teacher", "Teaching", "Attendance & Records", "/teacher/attendance", "ClipboardList", PermissionModule.SessionCalendarManagement),
            ("teacher", "Teaching", "Demo Feedback", "/teacher/demo-feedback", "ClipboardCheck", PermissionModule.SessionCalendarManagement),
            ("teacher", "My Account", "Leave Management", "/teacher/leave", "CalendarOff", PermissionModule.LeaveManagement),
            ("teacher", "My Account", "My Payout", "/teacher/payout", "Banknote", PermissionModule.Payouts),
            ("teacher", "My Account", "Resources", "/teacher/resources", "FolderOpen", PermissionModule.ContentAccessManagement),
            ("parent", null, "Dashboard", "/parent", "LayoutDashboard", null),
            ("parent", "Learning", "Schedule & Live Class", "/parent/schedule", "CalendarClock", PermissionModule.SessionCalendarManagement),
            ("parent", "Learning", "Resources & Recordings", "/parent/resources", "FolderOpen", PermissionModule.ContentAccessManagement),
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
                    ConfigJson = Json(new() { ["fromAddress"] = "support@thereadernest.com", ["smtpHost"] = "", ["smtpPort"] = "587" }),
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
                    ConfigJson = Json(new() { ["domain"] = "meet.techmisai.com" }),
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
    }
}
