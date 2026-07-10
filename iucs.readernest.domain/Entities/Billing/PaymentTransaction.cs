using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Billing
{
    /// <summary>
    /// Gateway payment attempt against an invoice; successful transactions
    /// carry the generated receipt. System-generated, so BaseEntity (no user audit columns).
    /// </summary>
    [Index(nameof(GatewayTransactionId))]
    public class PaymentTransaction : BaseEntity
    {
        public Guid InvoiceId { get; set; }

        public Invoice Invoice { get; set; } = null!;

        public Guid PaymentAccountId { get; set; }

        public PaymentAccount PaymentAccount { get; set; } = null!;

        public decimal Amount { get; set; }

        [MaxLength(3)]
        public string Currency { get; set; } = "INR";

        public TransactionStatus Status { get; set; } = TransactionStatus.Pending;

        [MaxLength(256)]
        public string? GatewayTransactionId { get; set; }

        public PaymentMethod? Method { get; set; }

        public DateTime? PaidAtUtc { get; set; }

        [MaxLength(50)]
        public string? ReceiptNumber { get; set; }

        [MaxLength(1000)]
        public string? ReceiptUrl { get; set; }

        [MaxLength(500)]
        public string? FailureReason { get; set; }
    }
}
