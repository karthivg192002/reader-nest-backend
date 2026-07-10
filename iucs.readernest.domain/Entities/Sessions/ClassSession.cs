using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Sessions
{
    /// <summary>
    /// A scheduled class (regular or demo). Demo sessions have no batch.
    /// Reschedules and no-show carry-forwards link back to the originating session
    /// so the calendar and payout engine can trace history.
    /// </summary>
    [Index(nameof(ScheduledStartAtUtc))]
    [Index(nameof(Status))]
    public class ClassSession : AuditEntity
    {
        public Guid? BatchId { get; set; }

        public Batch? Batch { get; set; }

        public Guid TeacherProfileId { get; set; }

        public TeacherProfile TeacherProfile { get; set; } = null!;

        public SessionType Type { get; set; } = SessionType.Regular;

        public SessionStatus Status { get; set; } = SessionStatus.Scheduled;

        public DateTime ScheduledStartAtUtc { get; set; }

        public DateTime ScheduledEndAtUtc { get; set; }

        public DateTime? ActualStartAtUtc { get; set; }

        public DateTime? ActualEndAtUtc { get; set; }

        /// <summary>Video conference room identifier (no manual meeting links; one-click join).</summary>
        [MaxLength(128)]
        public string? MeetingRoomId { get; set; }

        public Guid? RescheduledFromSessionId { get; set; }

        public ClassSession? RescheduledFromSession { get; set; }

        public Guid? CarriedForwardFromSessionId { get; set; }

        public ClassSession? CarriedForwardFromSession { get; set; }

        [MaxLength(500)]
        public string? CancellationReason { get; set; }

        [MaxLength(2000)]
        public string? Summary { get; set; }
    }
}
