using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Admission
{
    /// <summary>
    /// Mandatory teacher feedback captured immediately after every demo class,
    /// before the demo session can be closed. Reviewed by the admission team
    /// to drive course recommendation and conversion.
    /// </summary>
    [Index(nameof(DemoBookingId), IsUnique = true)]
    public class DemoFeedback : BaseEntity
    {
        public Guid DemoBookingId { get; set; }

        public DemoBooking DemoBooking { get; set; } = null!;

        public Guid TeacherProfileId { get; set; }

        public TeacherProfile TeacherProfile { get; set; } = null!;

        [MaxLength(200)]
        public string AcademicLevel { get; set; } = null!;

        [MaxLength(2000)]
        public string Strengths { get; set; } = null!;

        [MaxLength(2000)]
        public string ImprovementAreas { get; set; } = null!;

        public Guid? RecommendedCourseId { get; set; }

        public Course? RecommendedCourse { get; set; }

        public CourseType SuggestedBatchType { get; set; }

        [MaxLength(2000)]
        public string? Remarks { get; set; }

        public DateTime SubmittedAtUtc { get; set; }
    }
}
