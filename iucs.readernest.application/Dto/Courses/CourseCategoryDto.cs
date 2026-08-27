using System.ComponentModel.DataAnnotations;

namespace iucs.readernest.application.Dto.Courses
{
    public class CourseCategoryDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = null!;

        public string? Description { get; set; }

        public Guid DepartmentId { get; set; }

        public string DepartmentName { get; set; } = null!;
    }

    public class CreateCourseCategoryRequest
    {
        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = null!;

        [MaxLength(500)]
        public string? Description { get; set; }

        [Required]
        public Guid DepartmentId { get; set; }
    }
}
