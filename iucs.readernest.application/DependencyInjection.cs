using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Helper;
using iucs.readernest.application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace iucs.readernest.application
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddApplication(this IServiceCollection services)
        {
            services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();
            services.AddScoped<IAuditLogService, AuditLogService>();
            services.AddScoped<INotificationService, NotificationService>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<ICourseService, CourseService>();
            services.AddScoped<IBatchService, BatchService>();
            services.AddScoped<ISessionService, SessionService>();
            services.AddScoped<IDemoBookingService, DemoBookingService>();
            services.AddScoped<IResourceService, ResourceService>();
            services.AddScoped<IBillingService, BillingService>();
            services.AddScoped<IPayoutService, PayoutService>();
            return services;
        }
    }
}
