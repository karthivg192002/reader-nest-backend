using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Courses
{
    /// <summary>Shared shape for create and update of a course.</summary>
    public class SaveCourseRequest
    {
        [Required]
        public Guid CourseCategoryId { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = null!;

        [MaxLength(1000)]
        public string? Description { get; set; }

        [Required]
        public CourseType Type { get; set; }

        /// <summary>Allowed values: 30, 45 or 60 (validated in the service).</summary>
        [Required]
        public int DurationMinutes { get; set; }

        [Required]
        [Range(0, 9_999_999)]
        public decimal Price { get; set; }

        [Required]
        [Range(1, 1000)]
        public int TotalSessions { get; set; }

        [Required]
        public Department Department { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
