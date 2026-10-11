using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using iucs.readernest.domain.Data;
using iucs.readernest.domain.Entities.Saas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.api.Controllers;

/// <summary>Idempotent recurring-billing callback. Provider adapters normalize events into metadata tenantId.</summary>
[ApiController, Route("api/saas/webhooks"), AllowAnonymous]
public sealed class SaasBillingWebhookController : ControllerBase
{
    private readonly ReaderNestDbContext _db;
    private readonly IConfiguration _config;
    public SaasBillingWebhookController(ReaderNestDbContext db, IConfiguration config) => (_db, _config) = (db, config);

    [HttpPost("{provider}")]
    public async Task<IActionResult> Receive(string provider, CancellationToken ct)
    {
        provider = provider.ToLowerInvariant();
        if (provider is not ("stripe" or "razorpay")) return NotFound();
        using var reader = new StreamReader(Request.Body, Encoding.UTF8); var body = await reader.ReadToEndAsync(ct);
        var secret = _config[$"SaasBilling:{provider}:WebhookSecret"];
        var signature = provider == "stripe" ? Request.Headers["Stripe-Signature"].FirstOrDefault() : Request.Headers["X-Razorpay-Signature"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(secret) || !ValidSignature(provider, body, secret, signature)) return Unauthorized();
        using var json = JsonDocument.Parse(body); var root = json.RootElement;
        var eventId = root.TryGetProperty("id", out var id) ? id.GetString() : null;
        var eventType = root.TryGetProperty("type", out var type) ? type.GetString() : root.TryGetProperty("event", out var ev) ? ev.GetString() : null;
        if (string.IsNullOrWhiteSpace(eventId) || string.IsNullOrWhiteSpace(eventType)) return BadRequest();
        if (await _db.SaasWebhookEvents.AnyAsync(x => x.Provider == provider && x.ExternalEventId == eventId, ct)) return Ok();

        var tenantText = FindString(root, "tenantId") ?? FindString(root, "tenant_id");
        Guid? tenantId = Guid.TryParse(tenantText, out var tid) ? tid : null;
        var sub = tenantId is null ? null : await _db.TenantSubscriptions.SingleOrDefaultAsync(x => x.TenantId == tenantId, ct);
        if (sub is not null)
        {
            if (eventType.Contains("paid") || eventType.Contains("succeeded") || eventType.Contains("active"))
            { sub.Status = "active"; sub.FailedPaymentAttempts = 0; sub.NextRetryAtUtc = null; sub.GraceEndsAtUtc = null; sub.CurrentPeriodEndsAtUtc = DateTime.UtcNow.AddMonths(1); }
            else if (eventType.Contains("failed"))
            { sub.Status = "past_due"; sub.FailedPaymentAttempts++; sub.NextRetryAtUtc = DateTime.UtcNow.AddDays(1); sub.GraceEndsAtUtc ??= DateTime.UtcNow.AddDays(7); }
            else if (eventType.Contains("cancel")) { sub.Status = "canceled"; sub.GraceEndsAtUtc ??= DateTime.UtcNow.AddDays(7); }
        }
        _db.SaasWebhookEvents.Add(new SaasWebhookEvent { Provider = provider, ExternalEventId = eventId, EventType = eventType, TenantId = tenantId, ProcessedAtUtc = DateTime.UtcNow });
        await _db.SaveChangesAsync(ct); return Ok();
    }

    private static bool ValidSignature(string provider, string body, string secret, string? signature)
    {
        if (string.IsNullOrWhiteSpace(signature)) return false;
        string payload = body; string supplied = signature;
        if (provider == "stripe")
        {
            var parts = signature.Split(',').Select(x => x.Split('=', 2)).Where(x => x.Length == 2).ToDictionary(x => x[0], x => x[1]);
            if (!parts.TryGetValue("t", out var timestamp) || !parts.TryGetValue("v1", out var stripeSignature)) return false;
            supplied = stripeSignature;
            if (!long.TryParse(timestamp, out var unix) || Math.Abs(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - unix) > 300) return false;
            payload = timestamp + "." + body;
        }
        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return supplied.Length == expected.Length && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied.ToLowerInvariant()), Encoding.UTF8.GetBytes(expected));
    }

    private static string? FindString(JsonElement value, string name)
    {
        if (value.ValueKind == JsonValueKind.Object)
            foreach (var item in value.EnumerateObject()) { if (item.NameEquals(name) && item.Value.ValueKind == JsonValueKind.String) return item.Value.GetString(); var nested = FindString(item.Value, name); if (nested is not null) return nested; }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) { var nested = FindString(item, name); if (nested is not null) return nested; }
        return null;
    }
}
