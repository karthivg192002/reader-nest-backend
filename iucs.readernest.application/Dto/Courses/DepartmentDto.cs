using System.ComponentModel.DataAnnotations;

namespace iucs.readernest.application.Dto.Courses
{
    public class DepartmentDto
    {
        public Guid Id { get; set; }

        public string Name { get; set; } = null!;

        public string? Description { get; set; }

        public bool IsActive { get; set; }
    }

    /// <summary>Shared shape for create and update of a department.</summary>
    public class SaveDepartmentRequest
    {
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = null!;

        [MaxLength(500)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
