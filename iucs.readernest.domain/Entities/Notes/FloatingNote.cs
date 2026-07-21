using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Notes
{
    /// <summary>
    /// One sticky note in a user's floating notes widget (Settings &amp; Branding → Widgets
    /// controls which portals show the widget at all). Private to the owning user —
    /// there is no sharing or admin visibility into note content.
    /// </summary>
    [Index(nameof(UserId))]
    public class FloatingNote : AuditEntity
    {
        public Guid UserId { get; set; }

        public User User { get; set; } = null!;

        public string Content { get; set; } = string.Empty;

        /// <summary>Hex color for the note card, e.g. "#1F6FE0"; null uses the UI default.</summary>
        [MaxLength(16)]
        public string? Color { get; set; }

        /// <summary>Display order within the owning user's note list.</summary>
        public int SortOrder { get; set; }
    }
}
