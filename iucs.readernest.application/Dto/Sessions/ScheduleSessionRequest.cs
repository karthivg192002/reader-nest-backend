using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Sessions
{
    public class ScheduleSessionRequest
    {
        /// <summary>Required for regular sessions; null for demo sessions.</summary>
        public Guid? BatchId { get; set; }

        [Required]
        public Guid TeacherProfileId { get; set; }

        public SessionType Type { get; set; } = SessionType.Regular;

        [Required]
        public DateTime ScheduledStartAtUtc { get; set; }

        [Required]
        public DateTime ScheduledEndAtUtc { get; set; }
    }

    public class RescheduleSessionRequest
    {
        [Required]
        public DateTime ScheduledStartAtUtc { get; set; }

        [Required]
        public DateTime ScheduledEndAtUtc { get; set; }

        /// <summary>Null keeps the session's current teacher — only set this to actually move
        /// the session to a different teacher (same "Edit session" action, not a separate one).</summary>
        public Guid? TeacherProfileId { get; set; }

        /// <summary>Null keeps the session's current batch. Demo bookings' own reschedule call
        /// never sets this — a demo has no batch to change.</summary>
        public Guid? BatchId { get; set; }
    }

    public class CancelSessionRequest
    {
        [Required]
        [MaxLength(500)]
        public string Reason { get; set; } = null!;
    }
}
