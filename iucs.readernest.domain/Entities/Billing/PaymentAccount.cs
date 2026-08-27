using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Common;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Billing
{
    /// <summary>
    /// A payment gateway account. The platform ships with two (Phonics, Maths) but any
    /// admin-added department needs its own account too, so every invoice/transaction
    /// routes through exactly one and revenue is recorded department-wise.
    /// Gateway credentials/secrets are NOT stored here — only an external reference.
    /// </summary>
    [Index(nameof(DepartmentId), IsUnique = true)]
    public class PaymentAccount : AuditEntity
    {
        [MaxLength(150)]
        public string Name { get; set; } = null!;

        public Guid DepartmentId { get; set; }

        public Department Department { get; set; } = null!;

        [MaxLength(100)]
        public string GatewayProvider { get; set; } = null!;

        /// <summary>Gateway-side merchant/account identifier; secrets live in configuration/secret store.</summary>
        [MaxLength(256)]
        public string GatewayAccountRef { get; set; } = null!;

        public bool IsActive { get; set; } = true;
    }
}
