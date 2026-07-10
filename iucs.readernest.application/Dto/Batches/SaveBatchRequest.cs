using System.ComponentModel.DataAnnotations;

namespace iucs.readernest.application.Dto.Batches
{
    /// <summary>Shared shape for create and update of a batch.</summary>
    public class SaveBatchRequest
    {
        [Required]
        public Guid CourseId { get; set; }

        [Required]
        public Guid TeacherProfileId { get; set; }

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = null!;

        [Required]
        [Range(1, 500)]
        public int Capacity { get; set; }

        public DateOnly? StartDate { get; set; }

        public DateOnly? EndDate { get; set; }
    }
}
