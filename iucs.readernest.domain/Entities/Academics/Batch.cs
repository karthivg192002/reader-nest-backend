using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Academics
{
    [Index(nameof(Status))]
    public class Batch : AuditEntity
    {
        public Guid CourseId { get; set; }

        public Course Course { get; set; } = null!;

        public Guid TeacherProfileId { get; set; }

        public TeacherProfile TeacherProfile { get; set; } = null!;

        [MaxLength(150)]
        public string Name { get; set; } = null!;

        /// <summary>Maximum students; 1 for individual batches.</summary>
        public int Capacity { get; set; }

        public BatchStatus Status { get; set; } = BatchStatus.Active;

        public DateOnly? StartDate { get; set; }

        public DateOnly? EndDate { get; set; }

        /// <summary>Overrides the course's own class length for this batch only (e.g. a
        /// paired/small-group batch running shorter sessions than the course default). Null
        /// means "use the course's DurationMinutes", the previous, only behaviour.</summary>
        public int? DurationMinutesOverride { get; set; }

        /// <summary>Set when all course sessions finish; anchors the 15-day recording access window.</summary>
        public DateTime? CompletedAtUtc { get; set; }

        /// <summary>How this batch's fee is expected to be collected. Defaults to
        /// FullPaymentDone so existing batches (created before this field existed) never
        /// spuriously trigger a reminder.</summary>
        public BatchPaymentPlanType PaymentPlanType { get; set; } = BatchPaymentPlanType.FullPaymentDone;

        /// <summary>Only set when PaymentPlanType is AfterSessions — the number of completed
        /// sessions after which a payment reminder goes out.</summary>
        public int? PaymentAfterSessionsCount { get; set; }

        /// <summary>Only set when PaymentPlanType is DueOnDate — the date a payment reminder
        /// goes out on/after.</summary>
        public DateOnly? PaymentDueDate { get; set; }

        /// <summary>Set once PaymentPlanReminderBackgroundService has sent this plan's reminder,
        /// so a since-passed due date or already-reached session count doesn't re-alert every
        /// cycle. Cleared back to null whenever the plan itself is edited (BatchService.UpdateAsync),
        /// so a corrected count/date re-arms it. Same de-duplication role as ClassSession's own
        /// RecordingMissingAlertSentAtUtc / OrphanedDemoAlertSentAtUtc.</summary>
        public DateTime? PaymentReminderSentAtUtc { get; set; }

        public ICollection<BatchEnrollment> Enrollments { get; set; } = new List<BatchEnrollment>();
    }
}
