using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Communication
{
    public class NotificationDto
    {
        public Guid Id { get; set; }

        public NotificationType Type { get; set; }

        /// <summary>The template this was rendered from (e.g. "batch-assignment"), null for
        /// hand-built/bulk emails — lets the frontend route a click more precisely than the
        /// coarse Type alone (several distinct templates share NotificationType.General).</summary>
        public string? TemplateKey { get; set; }

        public NotificationChannel Channel { get; set; }

        public string? Subject { get; set; }

        public string Body { get; set; } = null!;

        public bool IsRead { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime? ReadAtUtc { get; set; }
    }

    /// <summary>The signed-in user's recent notifications plus the unread tally for the bell badge.</summary>
    public class NotificationFeedDto
    {
        public int UnreadCount { get; set; }

        public IReadOnlyList<NotificationDto> Items { get; set; } = [];
    }
}
