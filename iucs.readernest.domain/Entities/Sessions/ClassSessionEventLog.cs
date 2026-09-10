using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Sessions
{
    /// <summary>
    /// Append-only trail of every lifecycle event a live class (demo or regular) goes
    /// through — teacher start/join/leave/end, student join/leave, no-shows and denied join
    /// attempts, expected and unexpected alike. Powers the IT Admin "Class Session Logs"
    /// screen; never touched by payout/attendance logic (see SessionAttendance, which stays
    /// the source of truth there) — this exists purely to be read, not acted on.
    /// </summary>
    [Index(nameof(ClassSessionId))]
    [Index(nameof(OccurredAtUtc))]
    [Index(nameof(EventType))]
    public class ClassSessionEventLog : BaseEntity
    {
        public Guid ClassSessionId { get; set; }

        public ClassSession ClassSession { get; set; } = null!;

        public ClassSessionEventType EventType { get; set; }

        /// <summary>Null for a session-level system event (e.g. a no-show marked against
        /// neither a specific joined teacher nor student connection).</summary>
        public ParticipantType? ParticipantType { get; set; }

        public Guid? TeacherProfileId { get; set; }

        public TeacherProfile? TeacherProfile { get; set; }

        public Guid? ChildId { get; set; }

        public Child? Child { get; set; }

        /// <summary>The signed-in account this event is attributed to, when there is one —
        /// null for a demo lead (no account) or a pure system event.</summary>
        public Guid? UserId { get; set; }

        /// <summary>Display-name snapshot, same idiom as EngagementEvent.ParticipantName —
        /// survives a later name change and covers demo leads with no Child/TeacherProfile row.</summary>
        [MaxLength(200)]
        public string? ParticipantName { get; set; }

        public DateTime OccurredAtUtc { get; set; }

        /// <summary>False flags this row for the "unexpected scenarios" view — a disconnect
        /// before the class was due to end, a denied join, a no-show, a session started well
        /// past its scheduled time, and so on.</summary>
        public bool IsExpected { get; set; } = true;

        /// <summary>Short human-readable context, e.g. "started 6 min after scheduled time" or
        /// "denied — not an enrolled participant".</summary>
        [MaxLength(500)]
        public string? Detail { get; set; }
    }
}
