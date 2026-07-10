using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Courses
{
    public class CourseDto
    {
        public Guid Id { get; set; }

        public Guid CourseCategoryId { get; set; }

        public string CategoryName { get; set; } = null!;

        public string Name { get; set; } = null!;

        public string? Description { get; set; }

        public CourseType Type { get; set; }

        public int DurationMinutes { get; set; }

        public decimal Price { get; set; }

        public int TotalSessions { get; set; }

        public Department Department { get; set; }

        public bool IsActive { get; set; }
    }
}
