using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Academics
{
    /// <summary>
    /// Business department (Phonics, Maths, and any admin-added ones). Each department maps
    /// to its own payment gateway account (<see cref="Billing.PaymentAccount"/>) and
    /// transactions are recorded department-wise -- previously a fixed 2-value enum, now
    /// admin-manageable so new subject areas (Hindi, Spoken English, Abacus, ...) don't need
    /// a code change + redeploy to add.
    /// </summary>
    [Index(nameof(Name), IsUnique = true)]
    public class Department : AuditEntity
    {
        [MaxLength(100)]
        public string Name { get; set; } = null!;

        [MaxLength(500)]
        public string? Description { get; set; }

        /// <summary>Deactivated departments stay for historical records (courses/invoices/payment
        /// accounts already using them) but drop out of "assign a department" pickers.</summary>
        public bool IsActive { get; set; } = true;
    }
}
