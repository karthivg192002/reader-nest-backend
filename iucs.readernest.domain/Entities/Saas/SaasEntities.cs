using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Saas;

[Index(nameof(Slug), IsUnique = true)]
public sealed class Tenant : AuditEntity
{
    [MaxLength(120)] public string Name { get; set; } = null!;
    [MaxLength(80)] public string Slug { get; set; } = null!;
    [MaxLength(20)] public string Status { get; set; } = "active";
    [MaxLength(120)] public string? CustomDomain { get; set; }
    [MaxLength(16)] public string PrimaryColor { get; set; } = "#4f46e5";
    [MaxLength(512)] public string? LogoUrl { get; set; }
}

[Index(nameof(TenantId), nameof(UserId), IsUnique = true)]
public sealed class TenantMembership : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid UserId { get; set; }
    [MaxLength(24)] public string Role { get; set; } = "member";
    public bool IsOwner { get; set; }
}

[Index(nameof(TokenHash), IsUnique = true)]
public sealed class TenantInvitation : AuditEntity
{
    public Guid TenantId { get; set; }
    [MaxLength(256)] public string Email { get; set; } = null!;
    [MaxLength(24)] public string Role { get; set; } = "member";
    [MaxLength(128)] public string TokenHash { get; set; } = null!;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}

[Index(nameof(TenantId), IsUnique = true)]
public sealed class TenantSubscription : AuditEntity
{
    public Guid TenantId { get; set; }
    [MaxLength(24)] public string PlanId { get; set; } = "growth";
    [MaxLength(24)] public string Status { get; set; } = "trialing";
    [MaxLength(24)] public string Provider { get; set; } = "manual";
    [MaxLength(128)] public string? ProviderCustomerId { get; set; }
    [MaxLength(128)] public string? ProviderSubscriptionId { get; set; }
    public DateTime? TrialEndsAtUtc { get; set; }
    public DateTime? CurrentPeriodEndsAtUtc { get; set; }
    public DateTime? CancelAtUtc { get; set; }
    public DateTime? GraceEndsAtUtc { get; set; }
    public int FailedPaymentAttempts { get; set; }
    public DateTime? NextRetryAtUtc { get; set; }
}

[Index(nameof(TenantId), nameof(PeriodStartUtc))]
public sealed class SaasInvoice : BaseEntity
{
    public Guid TenantId { get; set; }
    [MaxLength(64)] public string Number { get; set; } = null!;
    [MaxLength(24)] public string Status { get; set; } = "open";
    [MaxLength(3)] public string Currency { get; set; } = "INR";
    public decimal Amount { get; set; }
    public DateTime PeriodStartUtc { get; set; }
    public DateTime PeriodEndUtc { get; set; }
    public DateTime? PaidAtUtc { get; set; }
    [MaxLength(128)] public string? ProviderInvoiceId { get; set; }
    [MaxLength(1024)] public string? HostedInvoiceUrl { get; set; }
}

[Index(nameof(Provider), nameof(ExternalEventId), IsUnique = true)]
public sealed class SaasWebhookEvent : BaseEntity
{
    [MaxLength(24)] public string Provider { get; set; } = null!;
    [MaxLength(160)] public string ExternalEventId { get; set; } = null!;
    [MaxLength(80)] public string EventType { get; set; } = null!;
    public Guid? TenantId { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
}

[Index(nameof(TenantId), nameof(CreatedAtUtc))]
public sealed class SaasAuditRecord : BaseEntity
{
    public Guid TenantId { get; set; }
    public Guid? ActorUserId { get; set; }
    [MaxLength(80)] public string Action { get; set; } = null!;
    [MaxLength(80)] public string EntityType { get; set; } = null!;
    [MaxLength(64)] public string? EntityId { get; set; }
    public string? DetailJson { get; set; }
    [MaxLength(64)] public string? IpAddress { get; set; }
}

public sealed class TenantDataExport : AuditEntity
{
    public Guid TenantId { get; set; }
    [MaxLength(20)] public string Status { get; set; } = "pending";
    [MaxLength(1024)] public string? DownloadUrl { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
