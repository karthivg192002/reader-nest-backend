using iucs.readernest.domain.Data;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.api.Services;

/// <summary>Advances trials, cancellations and failed-payment retries independently of page visits.</summary>
public sealed class SaasLifecycleService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<SaasLifecycleService> _logger;
    public SaasLifecycleService(IServiceScopeFactory scopes, ILogger<SaasLifecycleService> logger) => (_scopes, _logger) = (scopes, logger);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<ReaderNestDbContext>(); var now = DateTime.UtcNow;
                var subs = await db.TenantSubscriptions.Where(x => x.Status != "active" || x.CancelAtUtc != null).ToListAsync(stoppingToken);
                foreach (var sub in subs)
                {
                    if (sub.Status == "trialing" && sub.TrialEndsAtUtc <= now) { sub.Status = "past_due"; sub.GraceEndsAtUtc ??= now.AddDays(7); }
                    if (sub.CancelAtUtc <= now) { sub.Status = "canceled"; sub.GraceEndsAtUtc ??= now.AddDays(7); }
                    if (sub.Status is "past_due" or "canceled" && sub.GraceEndsAtUtc is not null && sub.GraceEndsAtUtc <= now) sub.Status = "suspended";
                    if (sub.Status == "past_due" && (sub.NextRetryAtUtc is null || sub.NextRetryAtUtc <= now) && sub.FailedPaymentAttempts < 4)
                    { sub.FailedPaymentAttempts++; sub.NextRetryAtUtc = now.AddDays(Math.Pow(2, sub.FailedPaymentAttempts - 1)); }
                }
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested) { _logger.LogError(ex, "SaaS lifecycle processing failed."); }
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }
}
