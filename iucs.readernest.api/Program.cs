using System.Text;
using System.Text.Json.Serialization;
using iucs.readernest.api.Auth;
using iucs.readernest.api.Data;
using iucs.readernest.api.Middleware;
using iucs.readernest.api.Services;
using iucs.readernest.application;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.domain.Common;
using iucs.readernest.domain.Data;
using iucs.readernest.domain.Data.Interceptors;
using iucs.readernest.domain.Repository;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
        // Enums travel as their names ("Teacher", "Phonics"), matching how they are stored
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

// Cross-cutting platform services
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
builder.Services.AddScoped<AuditableEntityInterceptor>();

// Persistence
//builder.Services.AddDbContext<ReaderNestDbContext>((serviceProvider, options) =>
//    options.UseNpgsql(builder.Configuration.GetConnectionString("ReaderNestDb"))
//        .AddInterceptors(serviceProvider.GetRequiredService<AuditableEntityInterceptor>()));
//builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
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
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
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
// CRM integration: lead webhooks, no-op until Integrations:CrmWebhookUrl is set
builder.Services.AddHttpClient();
builder.Services.AddSingleton<ICrmNotifier, WebhookCrmNotifier>();
// Automated reports: weekly KPI digest to admins
builder.Services.AddHostedService<ReportsDigestBackgroundService>();

// Authentication: JWT bearer
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
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
        };
    });

// Real-time classroom layer (roster, whiteboard sync, quizzes, celebrations)
builder.Services.AddSignalR();

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
        policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

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
}

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<iucs.readernest.api.Hubs.ClassroomHub>("/hubs/classroom");

app.MapGet("/health", () => Results.Ok(new { status = "ok", timestampUtc = DateTime.UtcNow }));

await DatabaseInitializer.InitializeAsync(app.Services, app.Configuration);

app.Run();