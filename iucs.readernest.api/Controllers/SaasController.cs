using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using iucs.readernest.domain.Data;
using iucs.readernest.domain.Entities.Saas;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.api.Controllers;

[ApiController, Route("api/saas"), Authorize(Roles = nameof(UserRole.Admin))]
public sealed class SaasController : ControllerBase
{
    private readonly ReaderNestDbContext _db;
    private readonly IConfiguration _config;
    public SaasController(ReaderNestDbContext db, IConfiguration config) => (_db, _config) = (db, config);

    [HttpGet("overview")]
    public async Task<ActionResult<SaasOverviewDto>> Overview(CancellationToken ct)
    {
        var tenant = await TenantAsync(ct);
        var sub = await SubscriptionAsync(tenant.Id, ct);
        ApplyLifecycle(sub);
        await _db.SaveChangesAsync(ct);
        var plan = Plans.Find(sub.PlanId);
        var now = DateTime.UtcNow;
        var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var members = await _db.TenantMemberships.CountAsync(x => x.TenantId == tenant.Id, ct);
        var students = await _db.Children.CountAsync(ct);
        var sessions = await _db.ClassSessions.CountAsync(x => x.ScheduledStartAtUtc >= start, ct);
        var bytes = await _db.Resources.SumAsync(x => x.FileSizeBytes ?? 0, ct);
        return Ok(new SaasOverviewDto(
            new(tenant.Id.ToString(), tenant.Name, tenant.Slug, tenant.CustomDomain, tenant.PrimaryColor, tenant.LogoUrl),
            new(plan.Id, plan.Name, sub.Status, sub.TrialEndsAtUtc, plan.PriceMonthlyInr, sub.Provider,
                sub.CurrentPeriodEndsAtUtc, sub.CancelAtUtc, sub.GraceEndsAtUtc, sub.FailedPaymentAttempts, sub.NextRetryAtUtc),
            Plans.All,
            [Usage("team-members", "Team members", members, plan.TeamMembers),
             Usage("students", "Students", students, plan.Students),
             Usage("live-sessions", "Live sessions this month", sessions, plan.LiveSessionsPerMonth),
             Usage("storage", "Workspace storage (GB)", (int)Math.Ceiling(bytes / 1_073_741_824d), plan.StorageGb)],
            await MembersAsync(tenant.Id, ct),
            await _db.SaasInvoices.Where(x => x.TenantId == tenant.Id).OrderByDescending(x => x.CreatedAtUtc).Take(25)
                .Select(x => new InvoiceDto(x.Id, x.Number, x.Status, x.Amount, x.Currency, x.PeriodStartUtc, x.PeriodEndUtc, x.HostedInvoiceUrl)).ToListAsync(ct),
            await _db.TenantDataExports.Where(x => x.TenantId == tenant.Id).OrderByDescending(x => x.CreatedAtUtc).Take(10)
                .Select(x => new ExportDto(x.Id, x.Status, x.DownloadUrl, x.CreatedAtUtc, x.ExpiresAtUtc)).ToListAsync(ct)));
    }

    [HttpPut("workspace")]
    public async Task<IActionResult> Workspace(UpdateWorkspaceRequest request, CancellationToken ct)
    {
        var tenant = await TenantAsync(ct, true);
        var name = request.Name.Trim(); var slug = request.Slug.Trim().ToLowerInvariant();
        if (name.Length is < 2 or > 120 || !Regex.IsMatch(slug, "^[a-z0-9]+(?:-[a-z0-9]+)*$"))
            return BadRequest("Enter a valid workspace name and lowercase URL slug.");
        if (await _db.Tenants.AnyAsync(x => x.Id != tenant.Id && x.Slug == slug, ct)) return Conflict("That workspace URL is already in use.");
        tenant.Name = name; tenant.Slug = slug;
        Audit(tenant.Id, "workspace.updated", "tenant", tenant.Id, request);
        await _db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpPut("branding")]
    public async Task<IActionResult> Branding(UpdateBrandingRequest request, CancellationToken ct)
    {
        var tenant = await TenantAsync(ct, true);
        if (!Regex.IsMatch(request.PrimaryColor, "^#[0-9a-fA-F]{6}$")) return BadRequest("Primary colour must be a six-digit hex colour.");
        if (!string.IsNullOrWhiteSpace(request.CustomDomain) && Uri.CheckHostName(request.CustomDomain) != UriHostNameType.Dns) return BadRequest("Enter a valid custom domain.");
        tenant.PrimaryColor = request.PrimaryColor;
        tenant.LogoUrl = Optional(request.LogoUrl);
        tenant.CustomDomain = Optional(request.CustomDomain)?.ToLowerInvariant();
        Audit(tenant.Id, "branding.updated", "tenant", tenant.Id, request);
        await _db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpPost("members/invite")]
    public async Task<ActionResult<InvitationDto>> Invite(InviteRequest request, CancellationToken ct)
    {
        var tenant = await TenantAsync(ct, true);
        var email = request.Email.Trim().ToLowerInvariant();
        if (!Regex.IsMatch(email, "^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$")) return BadRequest("Enter a valid email address.");
        if (!new[] { "admin", "member", "billing" }.Contains(request.Role)) return BadRequest("Choose a valid member role.");
        var plan = Plans.Find((await SubscriptionAsync(tenant.Id, ct)).PlanId);
        var count = await _db.TenantMemberships.CountAsync(x => x.TenantId == tenant.Id, ct)
            + await _db.TenantInvitations.CountAsync(x => x.TenantId == tenant.Id && x.AcceptedAtUtc == null && x.RevokedAtUtc == null && x.ExpiresAtUtc > DateTime.UtcNow, ct);
        if (count >= plan.TeamMembers) return Conflict("Your team-member limit has been reached. Upgrade before inviting another member.");
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var invite = new TenantInvitation { TenantId = tenant.Id, Email = email, Role = request.Role, TokenHash = Hash(token), ExpiresAtUtc = DateTime.UtcNow.AddDays(7) };
        _db.TenantInvitations.Add(invite);
        Audit(tenant.Id, "member.invited", "invitation", invite.Id, new { email, request.Role });
        await _db.SaveChangesAsync(ct);
        return Ok(new InvitationDto(invite.Id, email, request.Role, invite.ExpiresAtUtc, token));
    }

    [HttpPost("invitations/{token}/accept")]
    public async Task<IActionResult> Accept(string token, CancellationToken ct)
    {
        var invite = await _db.TenantInvitations.SingleOrDefaultAsync(x => x.TokenHash == Hash(token), ct);
        if (invite is null || invite.RevokedAtUtc != null || invite.AcceptedAtUtc != null || invite.ExpiresAtUtc <= DateTime.UtcNow)
            return BadRequest("This invitation is invalid or has expired.");
        if (!string.Equals(User.FindFirstValue(ClaimTypes.Email), invite.Email, StringComparison.OrdinalIgnoreCase)) return Forbid();
        var uid = UserId();
        if (!await _db.TenantMemberships.AnyAsync(x => x.TenantId == invite.TenantId && x.UserId == uid, ct))
            _db.TenantMemberships.Add(new() { TenantId = invite.TenantId, UserId = uid, Role = invite.Role });
        invite.AcceptedAtUtc = DateTime.UtcNow;
        Audit(invite.TenantId, "member.joined", "membership", uid, null);
        await _db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpDelete("members/{id:guid}")]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        var tenant = await TenantAsync(ct, true);
        var member = await _db.TenantMemberships.SingleOrDefaultAsync(x => x.Id == id && x.TenantId == tenant.Id, ct);
        if (member is null) return NotFound();
        if (member.IsOwner) return Conflict("Transfer ownership before removing the workspace owner.");
        _db.TenantMemberships.Remove(member);
        Audit(tenant.Id, "member.removed", "membership", member.Id, null);
        await _db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpPut("subscription")]
    public async Task<IActionResult> ChangePlan(UpdatePlatformSubscriptionRequest request, CancellationToken ct)
    {
        var tenant = await TenantAsync(ct, true);
        var plan = Plans.All.FirstOrDefault(x => x.Id == request.PlanId.Trim().ToLowerInvariant());
        if (plan is null) return BadRequest("Choose a valid SaaS plan.");
        var usage = await CurrentUsage(tenant.Id, ct);
        if (usage.members > plan.TeamMembers || usage.students > plan.Students || usage.sessions > plan.LiveSessionsPerMonth || usage.storage > plan.StorageGb)
            return Conflict("Current usage exceeds that plan. Reduce usage before downgrading.");
        var sub = await SubscriptionAsync(tenant.Id, ct);
        sub.PlanId = plan.Id; sub.CancelAtUtc = null;
        if (request.StartTrial && sub.TrialEndsAtUtc is null) { sub.Status = "trialing"; sub.TrialEndsAtUtc = DateTime.UtcNow.AddDays(14); }
        Audit(tenant.Id, "subscription.plan_changed", "subscription", sub.Id, new { plan.Id });
        await _db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpPost("subscription/cancel")]
    public async Task<IActionResult> Cancel(CancelRequest request, CancellationToken ct)
    {
        var tenant = await TenantAsync(ct, true); var sub = await SubscriptionAsync(tenant.Id, ct);
        sub.CancelAtUtc = request.Immediately ? DateTime.UtcNow : sub.CurrentPeriodEndsAtUtc ?? DateTime.UtcNow.AddMonths(1);
        if (request.Immediately) { sub.Status = "canceled"; sub.GraceEndsAtUtc = DateTime.UtcNow.AddDays(7); }
        Audit(tenant.Id, "subscription.cancel_scheduled", "subscription", sub.Id, request);
        await _db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpPost("subscription/reactivate")]
    public async Task<IActionResult> Reactivate(CancellationToken ct)
    {
        var tenant = await TenantAsync(ct, true); var sub = await SubscriptionAsync(tenant.Id, ct);
        sub.CancelAtUtc = null; sub.GraceEndsAtUtc = null; sub.Status = sub.TrialEndsAtUtc > DateTime.UtcNow ? "trialing" : "active";
        Audit(tenant.Id, "subscription.reactivated", "subscription", sub.Id, null);
        await _db.SaveChangesAsync(ct); return NoContent();
    }

    [HttpPost("checkout")]
    public async Task<ActionResult<CheckoutDto>> Checkout(CheckoutRequest request, CancellationToken ct)
    {
        var tenant = await TenantAsync(ct, true); var plan = Plans.Find(request.PlanId);
        var provider = request.Provider.Trim().ToLowerInvariant();
        if (provider is not ("stripe" or "razorpay")) return BadRequest("Choose Stripe or Razorpay.");
        var secret = _config[$"SaasBilling:{provider}:SecretKey"] ?? _config[$"SaasBilling:{provider}:KeySecret"];
        if (string.IsNullOrWhiteSpace(secret)) return BadRequest($"{provider} is not configured. Add its server-side billing credentials.");
        var url = _config[$"SaasBilling:{provider}:CheckoutUrl:{plan.Id}"];
        if (string.IsNullOrWhiteSpace(url)) return BadRequest($"Configure the {provider} checkout URL for the {plan.Name} plan.");
        var sub = await SubscriptionAsync(tenant.Id, ct); sub.Provider = provider;
        Audit(tenant.Id, "checkout.started", "subscription", sub.Id, new { provider, plan.Id });
        await _db.SaveChangesAsync(ct); return Ok(new CheckoutDto(provider, url));
    }

    [HttpPost("exports")]
    public async Task<ActionResult<ExportDto>> Export(CancellationToken ct)
    {
        var tenant = await TenantAsync(ct, true);
        var export = new TenantDataExport { TenantId = tenant.Id, Status = "queued", ExpiresAtUtc = DateTime.UtcNow.AddDays(7) };
        _db.TenantDataExports.Add(export); Audit(tenant.Id, "export.requested", "export", export.Id, null);
        await _db.SaveChangesAsync(ct);
        return Accepted(new ExportDto(export.Id, export.Status, null, export.CreatedAtUtc, export.ExpiresAtUtc));
    }

    [HttpGet("tenants")]
    public async Task<ActionResult<IReadOnlyList<TenantChoiceDto>>> Tenants(CancellationToken ct)
    {
        var uid = UserId();
        return Ok(await (from m in _db.TenantMemberships join t in _db.Tenants on m.TenantId equals t.Id
            where m.UserId == uid orderby t.Name select new TenantChoiceDto(t.Id, t.Name, t.Slug, m.Role, m.IsOwner)).ToListAsync(ct));
    }

    [HttpGet("audit")]
    public async Task<ActionResult<IReadOnlyList<AuditDto>>> AuditLog(CancellationToken ct)
    {
        var tenant = await TenantAsync(ct, true);
        return Ok(await _db.SaasAuditRecords.Where(x => x.TenantId == tenant.Id).OrderByDescending(x => x.CreatedAtUtc).Take(100)
            .Select(x => new AuditDto(x.Id, x.Action, x.EntityType, x.EntityId, x.ActorUserId, x.CreatedAtUtc, x.IpAddress)).ToListAsync(ct));
    }

    private async Task<Tenant> TenantAsync(CancellationToken ct, bool adminOnly = false)
    {
        var uid = UserId();
        var requested = Guid.TryParse(Request.Headers["X-Tenant-Id"].FirstOrDefault(), out var id) ? id : (Guid?)null;
        var member = await _db.TenantMemberships.Where(x => x.UserId == uid && (requested == null || x.TenantId == requested))
            .OrderByDescending(x => x.IsOwner).FirstOrDefaultAsync(ct);
        if (member is null)
        {
            var tenant = await _db.Tenants.OrderBy(x => x.CreatedAtUtc).FirstOrDefaultAsync(ct);
            if (tenant is null) { tenant = new() { Name = "Meet to Manage Academy", Slug = "academy" }; _db.Tenants.Add(tenant); }
            member = new() { TenantId = tenant.Id, UserId = uid, Role = "admin", IsOwner = true };
            _db.TenantMemberships.Add(member); await _db.SaveChangesAsync(ct);
        }
        if (adminOnly && !member.IsOwner && member.Role != "admin") throw new UnauthorizedAccessException("Workspace administrator access is required.");
        return await _db.Tenants.SingleAsync(x => x.Id == member.TenantId, ct);
    }

    private async Task<TenantSubscription> SubscriptionAsync(Guid tenantId, CancellationToken ct)
    {
        var sub = await _db.TenantSubscriptions.SingleOrDefaultAsync(x => x.TenantId == tenantId, ct);
        if (sub is not null) return sub;
        sub = new() { TenantId = tenantId, PlanId = "growth", Status = "trialing", TrialEndsAtUtc = DateTime.UtcNow.AddDays(14), CurrentPeriodEndsAtUtc = DateTime.UtcNow.AddDays(14) };
        _db.TenantSubscriptions.Add(sub); return sub;
    }

    private static void ApplyLifecycle(TenantSubscription sub)
    {
        var now = DateTime.UtcNow;
        if (sub.Status == "trialing" && sub.TrialEndsAtUtc <= now) { sub.Status = "past_due"; sub.GraceEndsAtUtc ??= now.AddDays(7); }
        if (sub.CancelAtUtc <= now && sub.Status != "canceled") { sub.Status = "canceled"; sub.GraceEndsAtUtc ??= now.AddDays(7); }
        if (sub.Status is "past_due" or "canceled" && sub.GraceEndsAtUtc <= now) sub.Status = "suspended";
    }

    private async Task<(int members, int students, int sessions, int storage)> CurrentUsage(Guid tenantId, CancellationToken ct)
    {
        var now = DateTime.UtcNow; var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return (await _db.TenantMemberships.CountAsync(x => x.TenantId == tenantId, ct), await _db.Children.CountAsync(ct),
            await _db.ClassSessions.CountAsync(x => x.ScheduledStartAtUtc >= start, ct),
            (int)Math.Ceiling(await _db.Resources.SumAsync(x => x.FileSizeBytes ?? 0, ct) / 1_073_741_824d));
    }

    private async Task<List<MemberDto>> MembersAsync(Guid tenantId, CancellationToken ct) => await
        (from m in _db.TenantMemberships join u in _db.Users on m.UserId equals u.Id where m.TenantId == tenantId
         orderby m.IsOwner descending, u.FirstName select new MemberDto(m.Id, u.Id, u.FirstName + " " + u.LastName, u.Email, m.Role, m.IsOwner)).ToListAsync(ct);

    private void Audit(Guid tenantId, string action, string entity, Guid id, object? detail) =>
        _db.SaasAuditRecords.Add(new() { TenantId = tenantId, ActorUserId = UserId(), Action = action, EntityType = entity,
            EntityId = id.ToString(), DetailJson = detail is null ? null : JsonSerializer.Serialize(detail), IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() });
    private Guid UserId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static UsageDto Usage(string key, string label, int used, int limit) => new(key, label, used, limit, used >= limit);
}

public sealed record UpdateWorkspaceRequest(string Name, string Slug);
public sealed record UpdateBrandingRequest(string PrimaryColor, string? LogoUrl, string? CustomDomain);
public sealed record UpdatePlatformSubscriptionRequest(string PlanId, bool StartTrial = false);
public sealed record InviteRequest(string Email, string Role);
public sealed record CancelRequest(bool Immediately);
public sealed record CheckoutRequest(string Provider, string PlanId);
public sealed record WorkspaceDto(string Id, string Name, string Slug, string? CustomDomain, string PrimaryColor, string? LogoUrl);
public sealed record PlatformSubscriptionDto(string PlanId, string PlanName, string Status, DateTime? TrialEndsAtUtc, int PriceMonthlyInr, string Provider, DateTime? CurrentPeriodEndsAtUtc, DateTime? CancelAtUtc, DateTime? GraceEndsAtUtc, int FailedPaymentAttempts, DateTime? NextRetryAtUtc);
public sealed record UsageDto(string Key, string Label, int Used, int Limit, bool LimitReached);
public sealed record MemberDto(Guid Id, Guid UserId, string Name, string Email, string Role, bool IsOwner);
public sealed record InvitationDto(Guid Id, string Email, string Role, DateTime ExpiresAtUtc, string Token);
public sealed record InvoiceDto(Guid Id, string Number, string Status, decimal Amount, string Currency, DateTime PeriodStartUtc, DateTime PeriodEndUtc, string? HostedInvoiceUrl);
public sealed record ExportDto(Guid Id, string Status, string? DownloadUrl, DateTime CreatedAtUtc, DateTime ExpiresAtUtc);
public sealed record CheckoutDto(string Provider, string Url);
public sealed record TenantChoiceDto(Guid Id, string Name, string Slug, string Role, bool IsOwner);
public sealed record AuditDto(Guid Id, string Action, string EntityType, string? EntityId, Guid? ActorUserId, DateTime CreatedAtUtc, string? IpAddress);
public sealed record SaasOverviewDto(WorkspaceDto Workspace, PlatformSubscriptionDto Subscription, IReadOnlyList<SaasPlanDto> Plans, IReadOnlyList<UsageDto> Usage, IReadOnlyList<MemberDto> Members, IReadOnlyList<InvoiceDto> Invoices, IReadOnlyList<ExportDto> Exports);
public sealed record SaasPlanDto(string Id, string Name, int PriceMonthlyInr, int TeamMembers, int Students, int LiveSessionsPerMonth, int StorageGb, string[] Features);

internal static class Plans
{
    internal static readonly SaasPlanDto Starter = new("starter", "Starter", 2499, 5, 100, 150, 25, ["Live classroom", "Parent portal", "Basic reports", "Email support"]);
    internal static readonly SaasPlanDto Growth = new("growth", "Growth", 5999, 20, 500, 1000, 250, ["Everything in Starter", "Recordings", "Automated billing", "Advanced reports", "Priority support"]);
    internal static readonly SaasPlanDto Scale = new("scale", "Scale", 12999, 100, 2500, 5000, 1000, ["Everything in Growth", "White-label branding", "Multiple branches", "API access", "Dedicated support"]);
    internal static readonly IReadOnlyList<SaasPlanDto> All = [Starter, Growth, Scale];
    internal static SaasPlanDto Find(string id) => All.FirstOrDefault(x => x.Id == id.Trim().ToLowerInvariant()) ?? Growth;
}
