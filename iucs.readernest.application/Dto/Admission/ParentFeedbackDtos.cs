using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Admission
{
    /// <summary>One thing the signed-in parent still owes us a rating for — drives the popup.</summary>
    public class PendingParentFeedbackDto
    {
        public ParentFeedbackKind Kind { get; set; }

        public Guid? DemoBookingId { get; set; }

        public Guid? BatchId { get; set; }

        public Guid? ChildId { get; set; }

        public string ChildName { get; set; } = null!;

        /// <summary>Course name for a completion prompt; null for a demo.</summary>
        public string? CourseName { get; set; }

        /// <summary>When the demo / course ended, so the newest prompt can be shown first.</summary>
        public DateTime EndedAtUtc { get; set; }
    }

    public class SubmitParentFeedbackRequest
    {
        [Required]
        public ParentFeedbackKind Kind { get; set; }

        public Guid? DemoBookingId { get; set; }

        public Guid? BatchId { get; set; }

        public Guid? ChildId { get; set; }

        [Range(1, 5)]
        public int Rating { get; set; }

        [MaxLength(1000)]
        public string? Comment { get; set; }
    }

    public class ParentFeedbackDto
    {
        public Guid Id { get; set; }

        public ParentFeedbackKind Kind { get; set; }

        public string ParentName { get; set; } = null!;

        public string ParentEmail { get; set; } = null!;

        public string ChildName { get; set; } = null!;

        /// <summary>Course (completion) or department (demo) the feedback is about, when known.</summary>
        public string? Subject { get; set; }

        public int Rating { get; set; }

        public string? Comment { get; set; }

        public DateTime SubmittedAtUtc { get; set; }
    }
}
