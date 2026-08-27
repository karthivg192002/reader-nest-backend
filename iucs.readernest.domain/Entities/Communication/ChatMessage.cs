using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Communication
{
    /// <summary>
    /// One turn of a user's "Ask a Doubt" chatbot history. High-volume and system-generated
    /// (every ask/answer pair writes two rows), so BaseEntity rather than AuditEntity — mirrors
    /// Notification. Private to the owning user, same visibility rule as FloatingNote.
    /// </summary>
    [Index(nameof(UserId), nameof(CreatedAtUtc))]
    public class ChatMessage : BaseEntity
    {
        public Guid UserId { get; set; }

        public User User { get; set; } = null!;

        public ChatMessageSender Sender { get; set; }

        public string Text { get; set; } = string.Empty;

        /// <summary>Set on a Bot message when it answered from a known FAQ; null for an escalated one.</summary>
        public Guid? MatchedFaqId { get; set; }

        public ChatFaq? MatchedFaq { get; set; }

        /// <summary>
        /// User feedback on a Bot answer — null until rated. A matched FAQ can still be the
        /// wrong answer (e.g. an overly broad keyword overlap), so "helpful: false" escalates
        /// to a teacher just like a no-match would, instead of trusting the match blindly.
        /// </summary>
        public bool? WasHelpful { get; set; }
    }
}
