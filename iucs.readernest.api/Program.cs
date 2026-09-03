using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using iucs.readernest.api.Auth;
using iucs.readernest.api.Data;
using iucs.readernest.api.Middleware;
using iucs.readernest.api.Services;
using iucs.readernest.application;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.application.Common.Options;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Common;
using iucs.readernest.domain.Data;
using iucs.readernest.domain.Data.Interceptors;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        // Enums travel as their names ("Teacher", "Overdue"), matching how they are stored
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

// Cross-cutting platform services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<AuditableEntityInterceptor>();

// Persistence
var connectionString =
    builder.Configuration.GetConnectionString("ReaderNestDb") ??
    Environment.GetEnvironmentVariable("ConnectionStrings__ReaderNestDb");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Database connection string is missing.");
}

builder.Services.AddDbContext<ReaderNestDbContext>((serviceProvider, options) =>
    options.UseNpgsql(connectionString)
           .AddInterceptors(serviceProvider.GetRequiredService<AuditableEntityInterceptor>()));

builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Application services + API-layer implementations of its abstractions
builder.Services.AddApplication();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
// Real SMTP delivery driven by the DB "email" integration config (Settings →
// Integrations); logs and no-ops safely when that integration is off/unconfigured.
builder.Services.AddScoped<IEmailSender, SmtpEmailSender>();
// WhatsApp Business Cloud API delivery, driven by the DB "whatsapp" integration.
builder.Services.AddScoped<IWhatsAppSender, WhatsAppSender>();
// SMS delivery (MSG91/Twilio), driven by the DB "sms" integration.
builder.Services.AddScoped<ISmsSender, SmsSender>();
// S3-compatible object storage (Hetzner Object Storage) everywhere — dev and prod both, so
// an upload made locally behaves identically to one made against the real deployment instead
// of silently depending on which environment you're in. Configured via Storage:S3:* (real
// credentials come from user-secrets locally, environment variables in prod — never committed).
builder.Services.AddSingleton<IFileStorage, S3FileStorage>();
// Parses uploaded bulk-import spreadsheets (.csv/.xlsx) for Users/Students/Departments/
// Courses/Package Plans/Quiz Questions — stateless, so singleton is fine.
builder.Services.AddSingleton<IBulkFileReader, BulkFileReader>();
// Signs room-scoped Jitsi join tokens from the DB "jitsi" integration's appId/appSecret;
// no-ops (null token, unsigned join) until an admin sets them — see JITSI_ARCHITECTURE.md.
builder.Services.AddSingleton<IJitsiTokenService, JitsiTokenService>();
// Renders the "Bill of Supply" invoice PDF (QuestPDF) — stateless beyond a static embedded
// logo loaded once, so singleton is fine.
builder.Services.AddSingleton<IInvoicePdfGenerator, InvoicePdfGenerator>();
// Server Monitoring dashboard: polls each server's rn-status agent (Monitoring:Servers config).
// Short timeout so one down/slow server can't stall the whole summary request.
builder.Services.AddHttpClient("Prometheus", client => client.Timeout = TimeSpan.FromSeconds(5));
builder.Services.AddScoped<IPrometheusClient, PrometheusClient>();
// Dual-gateway abstraction: the dispatcher routes to Razorpay/Cashfree using live
// credentials from Settings → Integrations, and falls back to the simulated gateway
// while an integration is disabled or its keys are blank.
builder.Services.AddSingleton<SimulatedPaymentGateway>();
builder.Services.AddScoped<iucs.readernest.api.Services.Payments.IGatewayAdapter, iucs.readernest.api.Services.Payments.RazorpayGateway>();
builder.Services.AddScoped<iucs.readernest.api.Services.Payments.IGatewayAdapter, iucs.readernest.api.Services.Payments.CashfreeGateway>();
builder.Services.AddScoped<IPaymentGateway, iucs.readernest.api.Services.Payments.PaymentGatewayDispatcher>();
// Auto billing: recurring invoice generation + overdue flagging + fee suspension
builder.Services.AddHostedService<BillingBackgroundService>();
// Session reminders, delayed-session alerts
builder.Services.AddHostedService<SessionReminderBackgroundService>();
// Automatic no-show detection: flags a session once its grace period elapses with one
// side never having joined, instead of relying solely on a human clicking "Mark No-Show"
builder.Services.AddHostedService<NoShowDetectionBackgroundService>();
// CRM integration: lead webhooks, no-op until Integrations:CrmWebhookUrl is set
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ICrmNotifier, WebhookCrmNotifier>();
// Automated reports: weekly KPI digest to admins
builder.Services.AddHostedService<ReportsDigestBackgroundService>();
// Progress reports: seeds an empty monthly draft per active child on the 1st
builder.Services.AddHostedService<ProgressReportsBackgroundService>();

// Authentication: JWT bearer
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<MonitoringOptions>(builder.Configuration.GetSection(MonitoringOptions.SectionName));
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Missing 'Jwt' configuration section.");
if (string.IsNullOrWhiteSpace(jwt.SigningKey) || Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
{
    // Fail at startup with an actionable message instead of letting every request
    // die in the JWT handler with IDX10703 (zero-length key). HS256 needs >= 256 bits.
    throw new InvalidOperationException(
        "Jwt:SigningKey is missing or shorter than 32 bytes. " +
        "Set the Jwt__SigningKey environment variable (or appsettings) to a random secret of at least 32 characters.");
}
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };

        // SignalR websockets can't send an Authorization header: the classroom hub
        // authenticates via the standard access_token query parameter instead.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken)
                    && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
            // The JWT itself stays valid for up to AccessTokenMinutes (8h) regardless of what
            // happens to the account after it was issued. Re-resolving access here — not just
            // status — closes that window down to the request itself: a revoked permission, a
            // changed role, or a role preset's own permissions being edited now takes effect on
            // the very next request from anyone holding a token for that account, instead of
            // silently staying valid until the token expires or they happen to log in again.
            // Costs one indexed PK lookup (plus the account's permission rows) per authenticated
            // call — the same shape of cost this check already paid for the status-only version.
            OnTokenValidated = async context =>
            {
                var userIdClaim = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
                if (!Guid.TryParse(userIdClaim, out var userId))
                {
                    context.Fail("Token is missing a valid subject.");
                    return;
                }

                var authService = context.HttpContext.RequestServices.GetRequiredService<IAuthService>();
                var access = await authService.GetCurrentAccessAsync(userId);
                if (access is null || access.Status != UserStatus.Active)
                {
                    context.Fail("This account is no longer active.");
                    return;
                }

                if (context.Principal!.Identity is ClaimsIdentity identity)
                {
                    // The role claim drives [Authorize(Roles = ...)] checks; Admin already gets
                    // every permission implicitly (PermissionAuthorizationHandler), so it carries
                    // none of these claims to begin with — nothing to refresh for that role.
                    foreach (var claim in identity.FindAll(ClaimTypes.Role).ToList())
                    {
                        identity.RemoveClaim(claim);
                    }
                    identity.AddClaim(new Claim(ClaimTypes.Role, access.Role.ToString()));

                    foreach (var claim in identity.FindAll(JwtTokenService.PermissionClaimType).ToList())
                    {
                        identity.RemoveClaim(claim);
                    }
                    identity.AddClaims(access.Permissions.Select(p => new Claim(JwtTokenService.PermissionClaimType, p)));
                }
            },
        };
    });

// In-memory cache for read-heavy, rarely-changing lookups (email template rendering
// today; see EmailTemplateService) — process-local, fine for the current
// single-instance deployment.
builder.Services.AddMemoryCache();

// Real-time classroom layer (roster, whiteboard sync, quizzes, celebrations)
builder.Services.AddSignalR();

// Live Server Monitoring push (replaces the dashboard's client-side poll)
builder.Services.AddSingleton<iucs.readernest.api.Hubs.MonitoringConnectionTracker>();
builder.Services.AddHostedService<iucs.readernest.api.Services.MonitoringBroadcastService>();

// Brute-force protection on login: framework-provided rate limiting (built into
// ASP.NET Core since .NET 7, no extra package). Rejects immediately over the limit
// rather than queuing, so a flood gets a fast 429 instead of stacking up requests.
// Partitioned per client IP so one attacker can't lock out everyone else.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
            }));
    // Anti-spam on the public store "Enroll now" form — same shape as "login" but a
    // looser limit, since this is legitimate-traffic throttling, not brute-force defense.
    options.AddPolicy("store-inquiry", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
            }));
    // Self-service PIN reset (forgot-pin/reset-pin): anonymous and email-enumeration-adjacent
    // like "login", so it gets the same brute-force-defense shape rather than the looser
    // "store-inquiry" one.
    options.AddPolicy("pin-reset", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
            }));
});

// Authorization: module/action permission policies (Admin passes implicitly)
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

// Origins must be exact scheme+host values (no trailing slash, no paths); a
// trailing slash never matches the browser's Origin header, so trim it here.
var allowedOrigins = (builder.Configuration
    .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .Select(origin => origin?.Trim().TrimEnd('/') ?? string.Empty)
    .Where(origin => origin.Length > 0)
    .ToArray();
if (allowedOrigins.Contains("*"))
{
    // The policy below uses AllowCredentials(), which the CORS protocol forbids
    // combining with a wildcard origin — surface the fix instead of the framework's
    // generic startup crash.
    throw new InvalidOperationException(
        "Cors:AllowedOrigins must list explicit origins — '*' is not allowed because the CORS policy " +
        "sends credentials. Set Cors__AllowedOrigins__0 to the frontend's URL, e.g. https://app.example.com.");
}
builder.Services.AddCors(options =>
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .WithMethods("GET", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")
            .AllowCredentials()));

var app = builder.Build();

// Must run before anything that reads Connection.RemoteIpAddress (the login rate
// limiter below): the API is served through a TLS-terminating reverse proxy in
// production, so without this every request otherwise reports the proxy's IP,
// collapsing the per-client limiter into one shared counter. KnownNetworks/
// KnownProxies are cleared because the proxy's address isn't fixed in this
// container deployment; ForwardLimit keeps only the immediate hop trusted.
var forwardedHeadersOptions = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1,
};
// Defaults only trust loopback; the reverse proxy's actual address isn't fixed
// in this container deployment, so trust the immediate hop regardless of address.
forwardedHeadersOptions.KnownNetworks.Clear();
forwardedHeadersOptions.KnownProxies.Clear();
app.UseForwardedHeaders(forwardedHeadersOptions);

app.UseMiddleware<ExceptionHandlingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}
else
{
    // In development the SPA calls the plain-HTTP port; redirecting to HTTPS
    // breaks CORS preflight requests, so only redirect outside development.
    app.UseHttpsRedirection();
    app.UseHsts();

    // Security headers: scoped to non-development so they never fight the dev-only
    // Scalar/OpenAPI UI, which needs inline scripts/styles a strict CSP would block.
    // This is a pure JSON API in production, so the policy can be tight.
    app.Use(async (context, next) =>
    {
        context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
        context.Response.Headers.Append("X-Frame-Options", "DENY");
        context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
        context.Response.Headers.Append("Content-Security-Policy", "default-src 'none'; frame-ancestors 'none'");
        await next();
    });
}

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();
app.MapHub<iucs.readernest.api.Hubs.ClassroomHub>("/hubs/classroom");
app.MapHub<iucs.readernest.api.Hubs.MonitoringHub>("/hubs/monitoring");

app.MapGet("/health", () => Results.Ok(new { status = "ok", timestampUtc = DateTime.UtcNow }));

// Deliberately outside /api and unauthenticated -- the whole point of a short link is that
// whoever receives it (a parent with no account, on any device) can just tap it. Redirects
// straight to the real target (e.g. a personal Jitsi join URL with its own signed token)
// rather than round-tripping through the SPA first.
app.MapGet("/m/{slug}", async (
    string slug,
    iucs.readernest.application.Services.IShortLinkService shortLinks,
    CancellationToken cancellationToken) =>
{
    var target = await shortLinks.ResolveAsync(slug, cancellationToken);
    return target is null
        ? Results.NotFound("This link has expired or doesn't exist.")
        : Results.Redirect(target, permanent: false);
});

await DatabaseInitializer.InitializeAsync(app.Services, app.Configuration);

app.Run();