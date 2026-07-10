using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.domain.Entities.Resources
{
    /// <summary>
    /// Uploaded learning content (reading books, worksheets). Business rule: only
    /// worksheets are downloadable (IsDownloadable); reading books are view-only,
    /// and nothing is visible to a parent without a ResourceAccess grant from admin.
    /// </summary>
    public class Resource : AuditEntity
    {
        [MaxLength(200)]
        public string Title { get; set; } = null!;

        public ResourceType Type { get; set; }

        [MaxLength(1000)]
        public string FileUrl { get; set; } = null!;

        [MaxLength(100)]
        public string? MimeType { get; set; }

        public long? FileSizeBytes { get; set; }

        public Guid? CourseId { get; set; }

        public Course? Course { get; set; }

        public Guid? BatchId { get; set; }

        public Batch? Batch { get; set; }

        public bool IsDownloadable { get; set; }

        [MaxLength(1000)]
        public string? Description { get; set; }
    }
}
