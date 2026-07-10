using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.domain.Data;
using iucs.readernest.domain.Entities.Billing;
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
    }
}
