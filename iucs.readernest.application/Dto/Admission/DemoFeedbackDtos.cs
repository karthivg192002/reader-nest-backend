using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Admission
{
    /// <summary>Mandatory teacher feedback captured after every demo class.</summary>
    public class SubmitDemoFeedbackRequest
    {
        [Required]
        [MaxLength(200)]
        public string AcademicLevel { get; set; } = null!;

        [Required]
        [MaxLength(2000)]
        public string Strengths { get; set; } = null!;

        [Required]
        [MaxLength(2000)]
        public string ImprovementAreas { get; set; } = null!;

        public Guid? RecommendedCourseId { get; set; }

        [Required]
        public CourseType SuggestedBatchType { get; set; }

        [MaxLength(2000)]
        public string? Remarks { get; set; }
    }

    public class DemoFeedbackDto
    {
        public Guid Id { get; set; }

        public Guid DemoBookingId { get; set; }

        public string ChildName { get; set; } = null!;

        public string ParentName { get; set; } = null!;

        public Guid TeacherProfileId { get; set; }

        public string TeacherName { get; set; } = null!;

        public string AcademicLevel { get; set; } = null!;

        public string Strengths { get; set; } = null!;

        public string ImprovementAreas { get; set; } = null!;

        public Guid? RecommendedCourseId { get; set; }

        public string? RecommendedCourseName { get; set; }

        public CourseType SuggestedBatchType { get; set; }

        public string? Remarks { get; set; }

        public DateTime SubmittedAtUtc { get; set; }
    }
}
