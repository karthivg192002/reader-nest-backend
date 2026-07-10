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
    }

    public class CancelSessionRequest
    {
        [Required]
        [MaxLength(500)]
        public string Reason { get; set; } = null!;
    }
}
