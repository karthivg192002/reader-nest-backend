using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Sessions
{
    /// <summary>
    /// A recording made in a member's Personal Meeting Room. There is no ClassSession behind
    /// that room for a <see cref="SessionRecording"/> to attach to, so the recording belongs to
    /// the room's owner instead and only the owner can see it.
    /// </summary>
    [Index(nameof(OwnerId))]
    public class PersonalRoomRecording : BaseEntity
    {
        public Guid OwnerId { get; set; }

        public User Owner { get; set; } = null!;

        [MaxLength(1000)]
        public string StorageUrl { get; set; } = null!;

        public int? DurationSeconds { get; set; }
    }
}
