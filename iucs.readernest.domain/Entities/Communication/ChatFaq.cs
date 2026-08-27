using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;

namespace iucs.readernest.domain.Entities.Communication
{
    /// <summary>
    /// One entry in the "Ask a Doubt" chatbot's rule-based knowledge base. Admin-managed
    /// (Settings &amp; Branding-adjacent screen, see /admin/chatbot) — no external AI dependency,
    /// the bot just matches a user's question against Question/Keywords token overlap.
    /// </summary>
    public class ChatFaq : AuditEntity
    {
        [MaxLength(500)]
        public string Question { get; set; } = string.Empty;

        public string Answer { get; set; } = string.Empty;

        /// <summary>Comma-separated extra terms the matcher should treat as synonyms for this FAQ.</summary>
        [MaxLength(500)]
        public string? Keywords { get; set; }

        [MaxLength(100)]
        public string? Category { get; set; }

        public bool IsActive { get; set; } = true;

        /// <summary>Display order in the admin list and the widget's browse view.</summary>
        public int SortOrder { get; set; }
    }
}
