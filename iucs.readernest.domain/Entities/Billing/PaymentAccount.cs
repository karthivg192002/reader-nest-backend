using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Billing
{
    /// <summary>
    /// A payment gateway account. The platform runs two: one for the Phonics
    /// department and one for Maths; every invoice/transaction routes through
    /// exactly one account so revenue is recorded department-wise.
    /// Gateway credentials/secrets are NOT stored here — only an external reference.
    /// </summary>
    [Index(nameof(Department), IsUnique = true)]
    public class PaymentAccount : AuditEntity
    {
        [MaxLength(150)]
        public string Name { get; set; } = null!;

        public Department Department { get; set; }

        [MaxLength(100)]
        public string GatewayProvider { get; set; } = null!;

        /// <summary>Gateway-side merchant/account identifier; secrets live in configuration/secret store.</summary>
        [MaxLength(256)]
        public string GatewayAccountRef { get; set; } = null!;

        public bool IsActive { get; set; } = true;
    }
}
