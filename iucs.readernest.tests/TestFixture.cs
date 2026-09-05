using System.Data.Common;
using System.Text.Json;
using iucs.readernest.application.Common;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Dto.Billing;
using iucs.readernest.domain.Common;
using iucs.readernest.domain.Data;
using iucs.readernest.domain.Data.Interceptors;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Communication;
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

    /// <summary>Simulates an SMTP failure (e.g. a sender account's daily limit) to prove a
    /// caller treats email delivery as best-effort rather than letting it fail the request.</summary>
    public class ThrowingEmailSender : IEmailSender
    {
        public Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated SMTP failure.");
        }
    }

    public class FakeCrmNotifier : ICrmNotifier
    {
        public List<(string EventType, object Payload)> Pushed { get; } = [];

        public Task PushLeadEventAsync(string eventType, object payload, CancellationToken cancellationToken = default)
        {
            Pushed.Add((eventType, payload));
            return Task.CompletedTask;
        }
    }

    public class FakeWhatsAppSender : IWhatsAppSender
    {
        public List<(string To, string Message)> Sent { get; } = [];

        public Task SendAsync(string toPhone, string message, CancellationToken cancellationToken = default)
        {
            Sent.Add((toPhone, message));
            return Task.CompletedTask;
        }
    }

    public class FakeSmsSender : ISmsSender
    {
        public List<(string To, string Message)> Sent { get; } = [];

        public Task SendAsync(string toPhone, string message, CancellationToken cancellationToken = default)
        {
            Sent.Add((toPhone, message));
            return Task.CompletedTask;
        }
    }

    /// <summary>Real parsing (CSV/XLSX via ClosedXML) lives in the api project's BulkFileReader,
    /// which this test project doesn't reference — tests exercise the service-layer bulk-import
    /// logic directly by preloading the rows a real parse would have produced.</summary>
    public class FakeBulkFileReader : IBulkFileReader
    {
        public List<Dictionary<string, string>> Rows { get; set; } = [];

        public List<Dictionary<string, string>> ReadRows(Stream content, string fileName) => Rows;
    }

    /// <summary>Real rendering (QuestPDF) lives in the api project's InvoicePdfGenerator, which
    /// this test project doesn't reference — tests only need to prove GenerateInvoicePdfAsync
    /// resolves the right invoice/data and calls through, not that a real PDF comes out.</summary>
    public class FakeInvoicePdfGenerator : IInvoicePdfGenerator
    {
        public InvoicePdfData? LastRequest { get; private set; }

        public byte[] Generate(InvoicePdfData data)
        {
            LastRequest = data;
            return [1, 2, 3];
        }
    }

    public class FakePaymentGateway : IPaymentGateway
    {
        /// <summary>References set here report Paid on the next status poll (drives reconcile tests).</summary>
        public HashSet<string> PaidReferences { get; } = new();

        /// <summary>Lets concurrency tests prove (or disprove) a double-disbursement, not just a double DB row.</summary>
        public int RefundCallCount { get; private set; }

        /// <summary>
        /// Set to make RefundAsync throw, standing in for a gateway that errors or times out —
        /// the case where the disbursement's outcome is unknown and the refund must therefore
        /// NOT become approvable again.
        /// </summary>
        public Exception? RefundFailure { get; set; }

        public Task<PaymentLinkResult> CreatePaymentLinkAsync(
            Invoice invoice,
            PaymentAccount account,
            string? preferredMethodKey = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new PaymentLinkResult
            {
                Url = $"https://pay.test/{invoice.Id}",
                GatewayReference = $"TEST-{invoice.InvoiceNumber}",
            });
        }

        public Task<RefundResult> RefundAsync(
            PaymentTransaction transaction,
            PaymentAccount account,
            decimal amount,
            CancellationToken cancellationToken = default)
        {
            RefundCallCount++;
            if (RefundFailure is not null)
            {
                throw RefundFailure;
            }

            return Task.FromResult(new RefundResult { GatewayRefundId = $"TEST-REFUND-{transaction.Id}" });
        }

        public Task<GatewayPaymentStatus> GetPaymentStatusAsync(
            string gatewayReference,
            CancellationToken cancellationToken = default)
        {
            var state = PaidReferences.Contains(gatewayReference)
                ? GatewayPaymentState.Paid
                : GatewayPaymentState.Pending;
            return Task.FromResult(new GatewayPaymentStatus { State = state, PaymentId = $"pay_{gatewayReference}" });
        }

        public Task<InlineCheckoutResult> CreateInlineCheckoutAsync(
            Invoice invoice,
            PaymentAccount account,
            string methodKey,
            InlinePayerInfo payer,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new InlineCheckoutResult
            {
                KeyId = "rzp_test_fake",
                OrderId = $"order_TEST-{invoice.InvoiceNumber}",
                AmountMinor = (long)Math.Round((invoice.Amount - invoice.AmountPaid) * 100m),
                Currency = invoice.Currency,
                Description = $"Test order for {invoice.InvoiceNumber}",
                PrefillName = payer.Name,
                PrefillEmail = payer.Email,
            });
        }

        /// <summary>Signature "valid" verifies; anything else fails — drives both verify-flow tests.</summary>
        public Task<bool> VerifyInlineCheckoutAsync(
            string orderReference,
            string gatewayPaymentId,
            string signature,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(signature == "valid");
        }

        /// <summary>Keys listed here report as configured; everything else does not (default: everything).</summary>
        public HashSet<string>? UnconfiguredKeys { get; set; }

        public Task<bool> IsMethodConfiguredAsync(string integrationKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(UnconfiguredKeys is null || !UnconfiguredKeys.Contains(integrationKey));
    }

    /// <summary>
    /// Stands in for PostgreSQL aborting a SERIALIZABLE transaction it could not serialize
    /// (SQLSTATE 40001). SQLite never raises one, and UnitOfWork deliberately recognises the
    /// condition by SQLSTATE on the provider-neutral DbException rather than by Npgsql's
    /// exception type, so a fake carrying that state is a faithful trigger for the retry path.
    /// </summary>
    public sealed class FakeSerializationFailure : DbException
    {
        public FakeSerializationFailure()
            : base("could not serialize access due to read/write dependencies among transactions")
        {
        }

        public override string SqlState => "40001";
    }

    public class FakeTokenService : ITokenService
    {
        public TokenResult CreateToken(User user, IReadOnlyCollection<string> permissionClaims)
        {
            return new TokenResult { AccessToken = "test-token", ExpiresAtUtc = DateTime.UtcNow.AddHours(1) };
        }
    }

    /// <summary>Mirrors production's "unconfigured" state (no appId/appSecret) — always returns no token.</summary>
    public class FakeJitsiTokenService : IJitsiTokenService
    {
        public string? CreateToken(
            string domain,
            string? jitsiConfigJson,
            string room,
            string participantName,
            string? participantEmail,
            bool moderator,
            DateTime expiresAtUtc) => null;

        /// <summary>Defaults to false (mirrors "unconfigured"/invalid); a test that needs the
        /// finalize-recording path to succeed sets this true first.</summary>
        public bool ValidateFinalizeTokenResult { get; set; }

        public bool ValidateFinalizeToken(string? bearerToken, string? jitsiConfigJson, string expectedRoom)
            => ValidateFinalizeTokenResult;
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

            // Departments used to be a fixed 2-value enum; every smoke test that seeds a
            // Course/CourseCategory/PaymentAccount/etc. still assumes Phonics/Maths exist under
            // these well-known ids (same as DatabaseInitializer.SeedDepartmentsAsync in the real
            // app, which this in-memory fixture bypasses entirely via EnsureCreated()).
            Context.Departments.AddRange(
                new Department { Id = WellKnownDepartments.Phonics, Name = "Phonics", IsActive = true },
                new Department { Id = WellKnownDepartments.Maths, Name = "Maths", IsActive = true });

            // Mirrors DatabaseInitializer.SeedPermissionModulesAsync — RoleService/UserService/
            // AccessRequestService/MenuService all now validate an incoming module key against
            // this table (it stopped being a compile-time-checked enum on the wire once custom
            // modules became possible), so every smoke test using a built-in module needs it seeded.
            Context.PermissionModuleDefinitions.AddRange(
                Enum.GetValues<PermissionModule>().Select((m, i) => new PermissionModuleDefinition
                {
                    Key = m.ToString(),
                    Label = m.ToString(),
                    IsSystem = true,
                    SortOrder = i,
                }));

            // Same catalog production seeds, so smoke tests exercise real templated
            // content (Subject/HtmlBody) instead of EmailTemplateService's fallback text.
            Context.EmailTemplates.AddRange(EmailTemplateSeedData.All.Select(seed => new EmailTemplate
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
            Context.SaveChanges();
        }

        public FakeCurrentUser CurrentUser { get; } = new();

        public ReaderNestDbContext Context { get; }

        public IUnitOfWork UnitOfWork { get; }

        /// <summary>
        /// A second, independent DbContext/UnitOfWork on the same underlying SQLite
        /// connection — simulates the second scoped DbContext ASP.NET Core would hand a
        /// concurrent HTTP request, for tests that need to prove (or disprove) a
        /// time-of-check-to-time-of-use race between two "simultaneous" requests.
        /// </summary>
        public (ReaderNestDbContext Context, IUnitOfWork UnitOfWork) CreateConcurrentSession()
        {
            var options = new DbContextOptionsBuilder<ReaderNestDbContext>()
                .UseSqlite(_connection)
                .AddInterceptors(new AuditableEntityInterceptor(CurrentUser))
                .Options;
            var context = new ReaderNestDbContext(options);
            return (context, new UnitOfWork(context));
        }

        public async Task<User> SeedUserAsync(
            string email,
            string pinHash,
            UserRole role = UserRole.Parent,
            UserStatus status = UserStatus.Active)
        {
            var user = new User
            {
                Email = email,
                PinHash = pinHash,
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
