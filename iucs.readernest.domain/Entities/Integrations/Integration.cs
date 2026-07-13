using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Integrations
{
    /// <summary>
    /// Master record for one third-party integration (Email, WhatsApp, Razorpay,
    /// Cashfree, Zoom, Jitsi Meet, ...). Admin-managed reference/config data;
    /// runtime services still read their own credentials from appsettings until
    /// those are wired to read from here.
    /// </summary>
    [Index(nameof(Key), IsUnique = true)]
    public class Integration : AuditEntity
    {
        /// <summary>Stable kebab-case identifier, e.g. "razorpay".</summary>
        [MaxLength(64)]
        public string Key { get; set; } = null!;

        [MaxLength(100)]
        public string Name { get; set; } = null!;

        public IntegrationCategory Category { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        public bool IsEnabled { get; set; }

        /// <summary>Provider-specific fields (api keys, webhook URLs, ...) as a JSON object string.</summary>
        public string? ConfigJson { get; set; }

        /// <summary>Seeded integrations the platform ships with; protected from deletion.</summary>
        public bool IsSystem { get; set; }
    }
}
