using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Resources
{
    public class ResourceDto
    {
        public Guid Id { get; set; }

        public string Title { get; set; } = null!;

        public ResourceType Type { get; set; }

        public string? MimeType { get; set; }

        public long? FileSizeBytes { get; set; }

        public Guid? CourseId { get; set; }

        public Guid? BatchId { get; set; }

        /// <summary>Batch display name, when the resource is tied to a batch.</summary>
        public string? BatchName { get; set; }

        /// <summary>All batches this resource is visible to (multi-batch visibility).</summary>
        public IReadOnlyList<string> VisibleBatchNames { get; set; } = [];

        public bool IsDownloadable { get; set; }

        public string? Description { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }

    /// <summary>Metadata accompanying the uploaded file (multipart form fields).</summary>
    public class CreateResourceRequest
    {
        [Required]
        [MaxLength(200)]
        public string Title { get; set; } = null!;

        [Required]
        public ResourceType Type { get; set; }

        public Guid? CourseId { get; set; }

        public Guid? BatchId { get; set; }

        /// <summary>Uploader-chosen batches the resource is visible to (multi-batch visibility).</summary>
        public List<Guid> BatchIds { get; set; } = [];

        /// <summary>Business rule: only worksheets should be downloadable; books are view-only.</summary>
        public bool IsDownloadable { get; set; }

        [MaxLength(1000)]
        public string? Description { get; set; }
    }

    public class GrantResourceAccessRequest
    {
        [Required]
        [MinLength(1)]
        public List<Guid> ParentProfileIds { get; set; } = [];

        public bool VisibleOnDashboard { get; set; } = true;
    }
}
