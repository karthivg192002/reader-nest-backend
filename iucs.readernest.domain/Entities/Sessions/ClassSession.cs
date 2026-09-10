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

        /// <summary>How many times this same class has already auto-carried-forward from an
        /// unresolved no-show (0 for an original, never-carried session). Caps the chain —
        /// see SessionService.MarkNoShowCoreAsync's own comment — instead of an abandoned
        /// booking silently rescheduling itself one week later forever.</summary>
        public int CarryForwardCount { get; set; }

        [MaxLength(500)]
        public string? CancellationReason { get; set; }

        [MaxLength(2000)]
        public string? Summary { get; set; }

        /// <summary>Set once RecordingReconciliationBackgroundService has alerted admins that this
        /// completed session never got a recording — auto-record can succeed at *starting* with
        /// no error yet still fail later in the pipeline (Jibri crashing mid-capture, an upload
        /// failure, the finalize webhook never reaching this app), which nothing else catches;
        /// this both de-duplicates the alert and marks the gap as already investigated.</summary>
        public DateTime? RecordingMissingAlertSentAtUtc { get; set; }
    }
}
