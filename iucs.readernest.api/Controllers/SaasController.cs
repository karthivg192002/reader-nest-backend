using iucs.readernest.domain.Data;
using iucs.readernest.domain.Entities.Settings;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.api.Controllers;

[ApiController]
[Route("api/saas")]
[Authorize(Roles = nameof(UserRole.Admin))]
public sealed class SaasController : ControllerBase
{
    private readonly ReaderNestDbContext _db;

    public SaasController(ReaderNestDbContext db) => _db = db;

    [HttpGet("overview")]
    public async Task<ActionResult<SaasOverviewDto>> GetOverview(CancellationToken cancellationToken)
    {
        var settings = await _db.AppSettings
            .Where(x => x.Key.StartsWith("saas."))
            .ToDictionaryAsync(x => x.Key, x => x.Value, cancellationToken);

        var planId = Value(settings, "saas.plan", "growth");
        var plan = Plans.All.FirstOrDefault(x => x.Id == planId) ?? Plans.Growth;
        var status = Value(settings, "saas.status", "trialing");
        var trialEndsAt = ParseDate(Value(settings, "saas.trialEndsAtUtc", ""));
        var now = DateTime.UtcNow;
        if (status == "trialing" && trialEndsAt is not null && trialEndsAt <= now) status = "past_due";

        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var activeUsers = await _db.Users.CountAsync(x => x.Status == UserStatus.Active && x.Role != UserRole.Parent, cancellationToken);
        var students = await _db.Children.CountAsync(cancellationToken);
        var sessions = await _db.ClassSessions.CountAsync(x => x.ScheduledStartAtUtc >= monthStart, cancellationToken);
        var storedBytes = await _db.Resources.SumAsync(x => x.FileSizeBytes ?? 0, cancellationToken);
        var storageGb = (int)Math.Ceiling(storedBytes / 1_073_741_824d);

        return Ok(new SaasOverviewDto(
            new WorkspaceDto(
                Value(settings, "saas.workspaceId", "default"),
                Value(settings, "saas.workspaceName", "Meet to Manage Academy"),
                Value(settings, "saas.workspaceSlug", "academy")),
            new PlatformSubscriptionDto(plan.Id, plan.Name, status, trialEndsAt, plan.PriceMonthlyInr),
            Plans.All,
            new[]
            {
                Usage("team-members", "Team members", activeUsers, plan.TeamMembers),
                Usage("students", "Students", students, plan.Students),
                Usage("live-sessions", "Live sessions this month", sessions, plan.LiveSessionsPerMonth),
                Usage("storage", "Workspace storage (GB)", storageGb, plan.StorageGb),
            }));
    }

    [HttpPut("workspace")]
    public async Task<ActionResult> UpdateWorkspace(UpdateWorkspaceRequest request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        var slug = request.Slug.Trim().ToLowerInvariant();
        if (name.Length is < 2 or > 120) return BadRequest("Workspace name must be between 2 and 120 characters.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(slug, "^[a-z0-9]+(?:-[a-z0-9]+)*$"))
            return BadRequest("Workspace URL must contain lowercase letters, numbers and single hyphens only.");

        await SetAsync("saas.workspaceName", name, cancellationToken);
        await SetAsync("saas.workspaceSlug", slug, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPut("subscription")]
    public async Task<ActionResult> UpdateSubscription(UpdatePlatformSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var plan = Plans.All.FirstOrDefault(x => x.Id == request.PlanId.Trim().ToLowerInvariant());
        if (plan is null) return BadRequest("Choose a valid SaaS plan.");

        await SetAsync("saas.plan", plan.Id, cancellationToken);
        await SetAsync("saas.status", request.StartTrial ? "trialing" : "active", cancellationToken);
        await SetAsync("saas.trialEndsAtUtc", request.StartTrial ? DateTime.UtcNow.AddDays(14).ToString("O") : "", cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task SetAsync(string key, string value, CancellationToken cancellationToken)
    {
        var setting = await _db.AppSettings.SingleOrDefaultAsync(x => x.Key == key, cancellationToken);
        if (setting is null)
        {
            _db.AppSettings.Add(new AppSetting { Category = SettingCategory.General, Key = key, Value = value, IsPublic = false });
        }
        else setting.Value = value;
    }

    private static string Value(IReadOnlyDictionary<string, string?> values, string key, string fallback) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static DateTime? ParseDate(string value) => DateTime.TryParse(value, out var parsed) ? parsed.ToUniversalTime() : null;
    private static UsageDto Usage(string key, string label, int used, int limit) => new(key, label, used, limit, limit > 0 && used >= limit);
}

public sealed record UpdateWorkspaceRequest(string Name, string Slug);
public sealed record UpdatePlatformSubscriptionRequest(string PlanId, bool StartTrial);
public sealed record WorkspaceDto(string Id, string Name, string Slug);
public sealed record PlatformSubscriptionDto(string PlanId, string PlanName, string Status, DateTime? TrialEndsAtUtc, int PriceMonthlyInr);
public sealed record UsageDto(string Key, string Label, int Used, int Limit, bool LimitReached);
public sealed record SaasOverviewDto(WorkspaceDto Workspace, PlatformSubscriptionDto Subscription, IReadOnlyList<SaasPlanDto> Plans, IReadOnlyList<UsageDto> Usage);
public sealed record SaasPlanDto(string Id, string Name, int PriceMonthlyInr, int TeamMembers, int Students, int LiveSessionsPerMonth, int StorageGb, string[] Features);

internal static class Plans
{
    internal static readonly SaasPlanDto Starter = new("starter", "Starter", 2499, 5, 100, 150, 25,
        ["Live classroom", "Parent portal", "Basic reports", "Email support"]);
    internal static readonly SaasPlanDto Growth = new("growth", "Growth", 5999, 20, 500, 1000, 250,
        ["Everything in Starter", "Recordings", "Automated billing", "Advanced reports", "Priority support"]);
    internal static readonly SaasPlanDto Scale = new("scale", "Scale", 12999, 100, 2500, 5000, 1000,
        ["Everything in Growth", "White-label branding", "Multiple branches", "API access", "Dedicated support"]);
    internal static readonly IReadOnlyList<SaasPlanDto> All = [Starter, Growth, Scale];
}
