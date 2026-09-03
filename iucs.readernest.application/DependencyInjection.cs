using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Helper;
using iucs.readernest.application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace iucs.readernest.application
{
    public static class DependencyInjection
    {
        // MonitoringOptions is bound in the API layer's Program.cs (like JwtOptions) — this
        // project doesn't reference the config-binder package needed for the IConfiguration
        // overload of Configure<T>.
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {
            services.AddScoped<IMonitoringService, MonitoringService>();
            services.AddScoped<IServerLogService, ServerLogService>();
            services.AddSingleton<IClassroomPresenceTracker, ClassroomPresenceTracker>();
            services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
            services.AddScoped<IAuditLogService, AuditLogService>();
            services.AddScoped<IShortLinkService, ShortLinkService>();
            services.AddScoped<INotificationService, NotificationService>();
            services.AddScoped<IEmailTemplateService, EmailTemplateService>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<ICourseService, CourseService>();
            services.AddScoped<IDepartmentService, DepartmentService>();
            services.AddScoped<IBatchService, BatchService>();
            services.AddScoped<ISessionService, SessionService>();
            services.AddScoped<IDemoBookingService, DemoBookingService>();
            services.AddScoped<IResourceService, ResourceService>();
            services.AddScoped<IGamificationService, GamificationService>();
            services.AddScoped<IBillingService, BillingService>();
            services.AddScoped<IPayoutService, PayoutService>();
            services.AddScoped<IProgressReportService, ProgressReportService>();
            services.AddScoped<IStoreService, StoreService>();
            services.AddScoped<IAcademicOpsService, AcademicOpsService>();
            services.AddScoped<IEnrollmentService, EnrollmentService>();
            services.AddScoped<IParentPortalService, ParentPortalService>();
            services.AddScoped<IReportsService, ReportsService>();
            services.AddScoped<ISettingsService, SettingsService>();
            services.AddScoped<IMenuService, MenuService>();
            services.AddScoped<IRoleService, RoleService>();
            services.AddScoped<IPermissionModuleService, PermissionModuleService>();
            services.AddScoped<IIntegrationService, IntegrationService>();
            services.AddScoped<IFloatingNoteService, FloatingNoteService>();
            services.AddScoped<IAccessRequestService, AccessRequestService>();
            services.AddScoped<IQuizQuestionService, QuizQuestionService>();
            services.AddScoped<IWhiteboardActivityService, WhiteboardActivityService>();
            services.AddScoped<IChatbotService, ChatbotService>();
            return services;
        }
    }
}
