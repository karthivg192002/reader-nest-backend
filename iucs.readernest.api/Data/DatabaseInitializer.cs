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
            await SeedMenusAsync(context);
            await SeedIntegrationsAsync(context);

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

            context.PaymentAccounts.AddRange(
                new PaymentAccount
                {
                    Name = "Phonics Department Account",
                    Department = Department.Phonics,
                    GatewayProvider = "pending-client-decision",
                    GatewayAccountRef = "phonics-account",
                },
                new PaymentAccount
                {
                    Name = "Maths Department Account",
                    Department = Department.Maths,
                    GatewayProvider = "pending-client-decision",
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
                    // column existed; leave everything else the admin may have edited.
                    if (string.IsNullOrWhiteSpace(current.DefaultRoute))
                    {
                        current.DefaultRoute = seed.DefaultRoute;
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
                ]),
                ("parent", "Parent", "Family account holder; managed through the parent portal.", "/parent", []),
                ("sub-admin", "Sub Admin", "Base delegated staff account; grant modules as needed.", "/subadmin", []),
                ("admission", "Admission", "Demo-to-enrollment pipeline and lead follow-up.", "/admission",
                [
                    Grant(PermissionModule.Admission, view: true, create: true, edit: true, approve: true),
                    Grant(PermissionModule.UserManagement, view: true),
                    Grant(PermissionModule.ReportsAnalytics, view: true),
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

        private static async Task SeedMenusAsync(ReaderNestDbContext context)
        {
            if (await context.MenuItems.AnyAsync())
            {
                return;
            }

            // (portal, section, label, path, lucide icon); orders derive from array position.
            (string Portal, string? Section, string Label, string Path, string Icon)[] items =
            [
                ("admin", null, "Dashboard", "/admin", "LayoutDashboard"),
                ("admin", "Academics", "Courses", "/admin/courses", "BookOpen"),
                ("admin", "Academics", "Batches", "/admin/batches", "Layers"),
                ("admin", "Academics", "Academic Calendar", "/admin/calendar", "CalendarDays"),
                ("admin", "Academics", "Sessions", "/admin/sessions", "CalendarClock"),
                ("admin", "People", "Users", "/admin/users", "Users"),
                ("admin", "People", "Roles & Permissions", "/admin/permissions", "ShieldCheck"),
                ("admin", "People", "Enrollment Review", "/admin/enrollments", "ClipboardCheck"),
                ("admin", "Content", "Content & Resources", "/admin/resources", "FolderOpen"),
                ("admin", "Finance", "Billing & Finance", "/admin/billing", "Receipt"),
                ("admin", "Finance", "Payment Gateway Mapping", "/admin/payment-mapping", "Landmark"),
                ("admin", "Finance", "Teacher Payouts", "/admin/payouts", "Wallet"),
                ("admin", "Finance", "Fee Suspension", "/admin/fee-suspension", "Ban"),
                ("admin", "Insights", "Reports & Analytics", "/admin/reports", "BarChart3"),
                ("admin", "Insights", "Bulk Email", "/admin/bulk-email", "Mail"),
                ("admin", "System", "Settings & Branding", "/admin/settings", "Settings"),
                ("teacher", null, "Dashboard", "/teacher", "LayoutDashboard"),
                ("teacher", "Teaching", "My Classes", "/teacher/classes", "CalendarClock"),
                ("teacher", "Teaching", "Live Classroom", "/teacher/live/s-1", "Video"),
                ("teacher", "Teaching", "Attendance & Records", "/teacher/attendance", "ClipboardList"),
                ("teacher", "Teaching", "Demo Feedback", "/teacher/demo-feedback", "ClipboardCheck"),
                ("teacher", "My Account", "Leave Management", "/teacher/leave", "CalendarOff"),
                ("teacher", "My Account", "My Payout", "/teacher/payout", "Banknote"),
                ("teacher", "My Account", "Resources", "/teacher/resources", "FolderOpen"),
                ("parent", null, "Dashboard", "/parent", "LayoutDashboard"),
                ("parent", "Learning", "Schedule & Live Class", "/parent/schedule", "CalendarClock"),
                ("parent", "Learning", "Resources & Recordings", "/parent/resources", "FolderOpen"),
                ("parent", "Account", "Payments & Billing", "/parent/billing", "CreditCard"),
                ("parent", "Account", "Notifications & Reports", "/parent/notifications", "Bell"),
                ("parent", "Account", "Add Child", "/parent/add-child", "UserPlus"),
                ("subadmin", null, "Dashboard", "/subadmin", "LayoutDashboard"),
                ("subadmin", "Access", "My Permissions", "/subadmin/permissions", "ShieldCheck"),
                ("subadmin", "Delegated Work", "Assigned Reports", "/subadmin/reports", "BarChart3"),
                ("subadmin", "Delegated Work", "Audit Log", "/subadmin/audit-log", "History"),
                ("admission", null, "Dashboard", "/admission", "LayoutDashboard"),
                ("admission", "Pipeline", "Demo Scheduling", "/admission/demo-scheduling", "CalendarClock"),
                ("admission", "Pipeline", "Demo Feedback", "/admission/demo-feedback", "ClipboardCheck"),
                ("admission", "Pipeline", "Conversion Board", "/admission/conversion", "KanbanSquare"),
                ("admission", "CRM", "Leads & Parents", "/admission/leads", "UserSearch"),
                ("admission", "CRM", "Payment Tracking", "/admission/payments", "Link2"),
                ("admission", "Insights", "Reports", "/admission/reports", "BarChart3"),
                ("coordinator", null, "Dashboard", "/coordinator", "LayoutDashboard"),
                ("coordinator", "Scheduling", "Academic Calendar", "/coordinator/calendar", "CalendarDays"),
                ("coordinator", "Scheduling", "Scheduling", "/coordinator/scheduling", "CalendarClock"),
                ("coordinator", "Scheduling", "Teacher Availability", "/coordinator/availability", "CalendarRange"),
                ("management", null, "Executive Overview", "/management", "LayoutDashboard"),
                ("management", "Performance", "Revenue & Courses", "/management/revenue", "TrendingUp"),
                ("management", "Performance", "Teacher & Batch Performance", "/management/performance", "Gauge"),
                ("management", "Insights", "Reports", "/management/reports", "FileBarChart"),
                ("student", null, "My Learning", "/student", "Sparkles"),
            ];

            var sectionOrders = new Dictionary<string, int>();
            var sortOrders = new Dictionary<string, int>();
            foreach (var (portal, section, label, path, icon) in items)
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
                });
        }
    }
}
