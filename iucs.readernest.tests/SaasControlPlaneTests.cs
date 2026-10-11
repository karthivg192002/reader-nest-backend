using iucs.readernest.domain.Entities.Saas;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace iucs.readernest.tests;

public sealed class SaasControlPlaneTests
{
    [Fact]
    public async Task Tenant_membership_and_subscription_are_persisted_separately()
    {
        using var db = new TestDatabase();
        var tenant = new Tenant { Name = "School A", Slug = "school-a" };
        db.Context.Tenants.Add(tenant);
        db.Context.TenantMemberships.Add(new TenantMembership { TenantId = tenant.Id, UserId = Guid.NewGuid(), Role = "admin", IsOwner = true });
        db.Context.TenantSubscriptions.Add(new TenantSubscription { TenantId = tenant.Id, PlanId = "growth", Status = "trialing", TrialEndsAtUtc = DateTime.UtcNow.AddDays(14) });
        await db.Context.SaveChangesAsync();

        Assert.Equal(tenant.Id, await db.Context.TenantMemberships.Select(x => x.TenantId).SingleAsync());
        Assert.Equal("growth", await db.Context.TenantSubscriptions.Select(x => x.PlanId).SingleAsync());
    }

    [Fact]
    public void Model_has_unique_tenant_membership_and_webhook_idempotency_keys()
    {
        using var db = new TestDatabase();
        var membership = db.Context.Model.FindEntityType(typeof(TenantMembership))!;
        var webhook = db.Context.Model.FindEntityType(typeof(SaasWebhookEvent))!;
        Assert.Contains(membership.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(["TenantId", "UserId"]));
        Assert.Contains(webhook.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(["Provider", "ExternalEventId"]));
    }
}
