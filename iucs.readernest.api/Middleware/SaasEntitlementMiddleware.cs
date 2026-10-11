using System.Security.Claims;
using iucs.readernest.domain.Data;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.api.Middleware;

/// <summary>Enforces tenant subscription access and creation quotas before business handlers run.</summary>
public sealed class SaasEntitlementMiddleware
{
    private readonly RequestDelegate _next;
    public SaasEntitlementMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ReaderNestDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated != true || HttpMethods.IsGet(context.Request.Method)
            || context.Request.Path.StartsWithSegments("/api/saas") || context.Request.Path.StartsWithSegments("/api/auth"))
        { await _next(context); return; }

        if (!Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)) { await _next(context); return; }
        var requested = Guid.TryParse(context.Request.Headers["X-Tenant-Id"].FirstOrDefault(), out var tid) ? tid : (Guid?)null;
        var membership = await db.TenantMemberships.Where(x => x.UserId == uid && (requested == null || x.TenantId == requested))
            .OrderByDescending(x => x.IsOwner).FirstOrDefaultAsync(context.RequestAborted);
        if (membership is null) { await _next(context); return; } // legacy accounts bootstrap in SaaS control centre

        var sub = await db.TenantSubscriptions.SingleOrDefaultAsync(x => x.TenantId == membership.TenantId, context.RequestAborted);
        if (sub?.Status == "suspended" || (sub?.Status is "past_due" or "canceled" && sub.GraceEndsAtUtc is not null && sub.GraceEndsAtUtc <= DateTime.UtcNow))
        { await Problem(context, 402, "Subscription access is paused. Update billing to continue."); return; }

        var limits = (sub?.PlanId ?? "growth") switch { "starter" => (5, 100, 150, 25), "scale" => (100, 2500, 5000, 1000), _ => (20, 500, 1000, 250) };
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
        if (HttpMethods.IsPost(context.Request.Method))
        {
            if (path.StartsWith("/api/users") && await db.TenantMemberships.CountAsync(x => x.TenantId == membership.TenantId) >= limits.Item1)
            { await Problem(context, 409, "The plan's team-member limit has been reached."); return; }
            if ((path.StartsWith("/api/children") || path.Contains("student")) && await db.Children.CountAsync() >= limits.Item2)
            { await Problem(context, 409, "The plan's student limit has been reached."); return; }
            if (path.StartsWith("/api/sessions"))
            {
                var now = DateTime.UtcNow; var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
                if (await db.ClassSessions.CountAsync(x => x.ScheduledStartAtUtc >= start) >= limits.Item3)
                { await Problem(context, 409, "The plan's monthly live-session limit has been reached."); return; }
            }
        }
        await _next(context);
    }

    private static async Task Problem(HttpContext context, int status, string detail)
    {
        context.Response.StatusCode = status; context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(new { type = "https://httpstatuses.com/" + status, title = "SaaS entitlement", status, detail });
    }
}
