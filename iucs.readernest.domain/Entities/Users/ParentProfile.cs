using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Billing;
using iucs.readernest.domain.Entities.Common;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Users
{
    /// <summary>
    /// Parent-specific state. One parent account holds multiple children
    /// on a single unified dashboard.
    /// </summary>
    [Index(nameof(UserId), IsUnique = true)]
    public class ParentProfile : BaseEntity
    {
        public Guid UserId { get; set; }

        public User User { get; set; } = null!;

        [MaxLength(500)]
        public string? Address { get; set; }

        /// <summary>Dashboard access is blocked until the mandatory first-login enrollment form is completed.</summary>
        public bool EnrollmentFormCompleted { get; set; }

        /// <summary>
        /// Optional admin override pinning this parent's payments to a specific
        /// department payment account; null routes by the invoice's own department.
        /// </summary>
        public Guid? PaymentAccountId { get; set; }

        public PaymentAccount? PaymentAccount { get; set; }

        public ICollection<Child> Children { get; set; } = new List<Child>();
    }
}
