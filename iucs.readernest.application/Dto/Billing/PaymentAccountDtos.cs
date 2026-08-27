using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Billing
{
    /// <summary>A department payment-gateway account with its live collection stats, for the admin Payment Gateway Mapping screen.</summary>
    public class PaymentAccountDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = null!;

        public Guid DepartmentId { get; set; }

        public string DepartmentName { get; set; } = null!;

        public string GatewayProvider { get; set; } = null!;

        public string GatewayAccountRef { get; set; } = null!;

        public bool IsActive { get; set; }

        public int TransactionCount { get; set; }

        public decimal TotalCollected { get; set; }

        public IReadOnlyList<PaymentAccountTransactionDto> RecentTransactions { get; set; } = [];
    }

    public class PaymentAccountTransactionDto
    {
        public Guid Id { get; set; }

        public string InvoiceNumber { get; set; } = null!;

        public string? StudentName { get; set; }

        public decimal Amount { get; set; }

        public TransactionStatus Status { get; set; }

        public DateTime DateUtc { get; set; }
    }

    /// <summary>Admin edit of a department account's gateway wiring (the seed ships with placeholder refs).</summary>
    public class UpdatePaymentAccountRequest
    {
        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = null!;

        /// <summary>Gateway integration key this account charges through ("razorpay", "cashfree").</summary>
        [Required]
        [MaxLength(100)]
        public string GatewayProvider { get; set; } = null!;

        /// <summary>Gateway-side merchant/account identifier; secrets stay in the integration config.</summary>
        [Required]
        [MaxLength(256)]
        public string GatewayAccountRef { get; set; } = null!;

        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Most orgs here run one real gateway account for the whole business, not a distinct
        /// one per department — defaults true so the easy path is "organization-wide," with
        /// per-department divergence as the opt-out for a genuine dual-gateway need. When true,
        /// every other department's account also gets this same GatewayProvider/GatewayAccountRef/
        /// IsActive (never its own Name — that stays department-specific for the card labels).
        /// </summary>
        public bool ApplyToAllDepartments { get; set; } = true;
    }

    public class SavePaymentMappingRequest
    {
        /// <summary>The parent's user account id (resolved to the parent profile server-side).</summary>
        [Required]
        public Guid ParentUserId { get; set; }

        [Required]
        public Guid PaymentAccountId { get; set; }
    }
}
