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
builder.Services.AddDbContext<ReaderNestDbContext>((serviceProvider, options) =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("ReaderNestDb"))
        .AddInterceptors(serviceProvider.GetRequiredService<AuditableEntityInterceptor>()));
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Application services + API-layer implementations of its abstractions
builder.Services.AddApplication();
builder.Services.AddScoped<ITokenService, JwtTokenService>();
builder.Services.AddScoped<IEmailSender, LoggingEmailSender>();
builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();
// Auto billing: recurring invoice generation + overdue flagging
builder.Services.AddHostedService<BillingBackgroundService>();

// Authentication: JWT bearer
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Missing 'Jwt' configuration section.");
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
    });

// Authorization: module/action permission policies (Admin passes implicitly)
builder.Services.AddAuthorization();
builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();

var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
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

app.MapGet("/health", () => Results.Ok(new { status = "ok", timestampUtc = DateTime.UtcNow }));

await DatabaseInitializer.InitializeAsync(app.Services, app.Configuration);

app.Run();
