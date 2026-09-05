using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Common
{
    /// <summary>
    /// A short, shareable redirect to an otherwise unwieldy URL — e.g. a personal Jitsi
    /// meeting link, which carries a long signed JWT in its fragment that reads as
    /// suspicious/broken when pasted into WhatsApp. Generic by design (TargetUrl is opaque),
    /// even though meeting links are the only caller today. Time-limited, matching whatever
    /// the target itself is only valid for — a short link outliving its target would just
    /// forward someone into a dead/expired join, not open a new security hole.
    /// </summary>
    [Index(nameof(Slug), IsUnique = true)]
    public class ShortLink : BaseEntity
    {
        [MaxLength(16)]
        public string Slug { get; set; } = null!;

        [MaxLength(2000)]
        public string TargetUrl { get; set; } = null!;

        public Guid CreatedByUserId { get; set; }

        public DateTime ExpiresAtUtc { get; set; }
    }
}
