using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Communication
{
    /// <summary>
    /// Admin-designed source of truth for every automated outbound email. Every system
    /// email send resolves its Subject/HtmlBody from the active row matching its Key —
    /// there is no hardcoded fallback text once seeded. Admin edits Subject/HtmlBody/IsActive
    /// via Settings → Email Templates; Key/Category/Placeholders are fixed by the code path
    /// that sends against them and are not admin-editable.
    /// </summary>
    [Index(nameof(Key), IsUnique = true)]
    public class EmailTemplate : AuditEntity
    {
        /// <summary>Stable kebab-case identifier the sending code looks up, e.g. "welcome-credentials".</summary>
        [MaxLength(100)]
        public string Key { get; set; } = null!;

        [MaxLength(150)]
        public string Name { get; set; } = null!;

        [MaxLength(500)]
        public string? Description { get; set; }

        /// <summary>Groups templates in the admin UI; matches the Notification.Type raised on send.</summary>
        public NotificationType Category { get; set; }

        [MaxLength(300)]
        public string Subject { get; set; } = null!;

        /// <summary>Full HTML with {{Token}} placeholders substituted at send time.</summary>
        public string HtmlBody { get; set; } = null!;

        /// <summary>JSON string array of the token names this template's sender supplies, e.g. ["FirstName","Email"].</summary>
        public string PlaceholdersJson { get; set; } = "[]";

        /// <summary>Inactive templates fall back to a minimal generic message rather than blocking the send.</summary>
        public bool IsActive { get; set; } = true;

        /// <summary>Seeded templates the platform ships with; protects Key from being changed.</summary>
        public bool IsSystem { get; set; }
    }
}
