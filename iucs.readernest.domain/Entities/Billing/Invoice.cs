using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Billing
{
    /// <summary>
    /// Auto-generated invoice routed to the department's payment account.
    /// Unpaid invoices past DueDate feed the fee-overdue check that triggers suspension.
    /// </summary>
    [Index(nameof(InvoiceNumber), IsUnique = true)]
    [Index(nameof(Status), nameof(DueDate))]
    public class Invoice : AuditEntity
    {
        [MaxLength(50)]
        public string InvoiceNumber { get; set; } = null!;

        public Guid ParentProfileId { get; set; }

        public ParentProfile ParentProfile { get; set; } = null!;

        public Guid? ChildId { get; set; }

        public Child? Child { get; set; }

        public Guid? SubscriptionId { get; set; }

        public Subscription? Subscription { get; set; }

        public Guid PaymentAccountId { get; set; }

        public PaymentAccount PaymentAccount { get; set; } = null!;

        public Department Department { get; set; }

        public decimal Amount { get; set; }

        public decimal AmountPaid { get; set; }

        [MaxLength(3)]
        public string Currency { get; set; } = "INR";

        public InvoiceStatus Status { get; set; } = InvoiceStatus.Pending;

        public DateOnly DueDate { get; set; }

        public DateTime IssuedAtUtc { get; set; }

        public DateTime? PaidAtUtc { get; set; }

        public ICollection<PaymentTransaction> Transactions { get; set; } = new List<PaymentTransaction>();
    }
}
