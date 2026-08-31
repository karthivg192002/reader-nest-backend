using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Activities;

namespace iucs.readernest.application.Dto.Activities
{
    public class WhiteboardActivityItemInput
    {
        [Required]
        [MaxLength(16)]
        public string Emoji { get; set; } = null!;

        [MaxLength(20)]
        public string? Label { get; set; }

        public bool IsTarget { get; set; }
    }

    public class SaveWhiteboardActivityRequest
    {
        /// <summary>Required when <see cref="CourseId"/> is not set — the activity is shared
        /// across every course in this department (and demo sessions in it). Ignored (derived
        /// from the course instead) when <see cref="CourseId"/> is set.</summary>
        public Guid? DepartmentId { get; set; }

        /// <summary>Optional — narrows the activity to one specific course. Leave unset to
        /// share it across the whole department.</summary>
        public Guid? CourseId { get; set; }

        [Required]
        public WhiteboardActivityMode Mode { get; set; }

        [Required]
        [MaxLength(500)]
        public string Prompt { get; set; } = null!;

        public int DisplayOrder { get; set; }

        [Required]
        [MinLength(2, ErrorMessage = "An activity needs at least 2 items.")]
        [MaxLength(8, ErrorMessage = "An activity can have at most 8 items.")]
        public List<WhiteboardActivityItemInput> Items { get; set; } = [];
    }

    public class WhiteboardActivityItemDto
    {
        public Guid Id { get; set; }

        public string Emoji { get; set; } = null!;

        public string? Label { get; set; }

        public bool IsTarget { get; set; }

        public int DisplayOrder { get; set; }
    }

    public class WhiteboardActivityDto
    {
        public Guid Id { get; set; }

        public Guid DepartmentId { get; set; }

        public string DepartmentName { get; set; } = null!;

        public Guid? CourseId { get; set; }

        public string? CourseName { get; set; }

        public WhiteboardActivityMode Mode { get; set; }

        public string Prompt { get; set; } = null!;

        public int DisplayOrder { get; set; }

        public IReadOnlyList<WhiteboardActivityItemDto> Items { get; set; } = [];
    }
}
