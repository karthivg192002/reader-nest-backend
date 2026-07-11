using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.domain.Common;
using iucs.readernest.domain.Data;
using iucs.readernest.domain.Data.Interceptors;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.tests
{
    public class FakeCurrentUser : ICurrentUserService
    {
        public Guid? UserId { get; set; }
    }

    public class FakeEmailSender : IEmailSender
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];

        public Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
        {
            Sent.Add((toEmail, subject, body));
            return Task.CompletedTask;
        }
    }

    public class FakePaymentGateway : IPaymentGateway
    {
        public Task<PaymentLinkResult> CreatePaymentLinkAsync(
            Invoice invoice,
            PaymentAccount account,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PaymentLinkResult
            {
                Url = $"https://pay.test/{invoice.Id}",
                GatewayReference = $"TEST-{invoice.InvoiceNumber}",
            });
        }
    }

    public class FakeTokenService : ITokenService
    {
        public TokenResult CreateToken(User user, IReadOnlyCollection<string> permissionClaims)
        {
            return new TokenResult { AccessToken = "test-token", ExpiresAtUtc = DateTime.UtcNow.AddHours(1) };
        }
    }

    /// <summary>
    /// Real ReaderNestDbContext (audit interceptor included) over SQLite in-memory,
    /// so smoke tests exercise the production model and save pipeline.
    /// </summary>
    public sealed class TestDatabase : IDisposable
    {
        private readonly SqliteConnection _connection;

        public TestDatabase()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            var options = new DbContextOptionsBuilder<ReaderNestDbContext>()
                .UseSqlite(_connection)
                .AddInterceptors(new AuditableEntityInterceptor(CurrentUser))
                .Options;

            Context = new ReaderNestDbContext(options);
            Context.Database.EnsureCreated();
            UnitOfWork = new UnitOfWork(Context);
        }

        public FakeCurrentUser CurrentUser { get; } = new();

        public ReaderNestDbContext Context { get; }

        public IUnitOfWork UnitOfWork { get; }

        public async Task<User> SeedUserAsync(
            string email,
            string passwordHash,
            UserRole role = UserRole.Parent,
            UserStatus status = UserStatus.Active)
        {
            var user = new User
            {
                Email = email,
                PasswordHash = passwordHash,
                FirstName = "Test",
                LastName = "User",
                Role = role,
                Status = status,
            };
            Context.Users.Add(user);
            await Context.SaveChangesAsync();
            return user;
        }

        public void Dispose()
        {
            Context.Dispose();
            _connection.Dispose();
        }
    }
}
