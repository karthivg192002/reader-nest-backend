using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;

namespace iucs.readernest.domain.Entities.Sessions
{
    /// <summary>
    /// A PDF deck the teacher uploaded to present live in this class — the "like Google Meet"
    /// present-a-deck flow. One per session, replaced (not versioned) on re-upload; the class's
    /// own live current-slide position is ephemeral hub state (ClassroomHub's CurrentSlide),
    /// not stored here, since it only ever matters while the class is live.
    /// </summary>
    public class SessionPresentation : BaseEntity
    {
        public Guid ClassSessionId { get; set; }

        public ClassSession ClassSession { get; set; } = null!;

        [MaxLength(1000)]
        public string StorageUrl { get; set; } = null!;

        [MaxLength(255)]
        public string OriginalFileName { get; set; } = null!;
    }
}
