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

        /// <summary>The folder this file sits in; null = top level.</summary>
        public Guid? FolderId { get; set; }

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

        /// <summary>Folder to upload into; omit for the top level.</summary>
        public Guid? FolderId { get; set; }

        /// <summary>Uploader-chosen batches the resource is visible to (multi-batch visibility).</summary>
        public List<Guid> BatchIds { get; set; } = [];

        /// <summary>Business rule: only worksheets should be downloadable; books are view-only.</summary>
        public bool IsDownloadable { get; set; }

        [MaxLength(1000)]
        public string? Description { get; set; }
    }

    public class UpdateResourceRequest
    {
        /// <summary>Business rule: only worksheets can be downloadable; reading books stay view-only regardless.</summary>
        public bool IsDownloadable { get; set; }
    }

    public class GrantResourceAccessRequest
    {
        [Required]
        [MinLength(1)]
        public List<Guid> ParentProfileIds { get; set; } = [];

        public bool VisibleOnDashboard { get; set; } = true;
    }

    public class StartLargeUploadRequest
    {
        [Required]
        [MaxLength(260)]
        public string FileName { get; set; } = null!;

        [MaxLength(100)]
        public string? ContentType { get; set; }

        [Range(1, long.MaxValue)]
        public long SizeBytes { get; set; }
    }

    public class LargeUploadDto
    {
        public string Key { get; set; } = null!;

        public string UploadId { get; set; } = null!;

        public long PartSizeBytes { get; set; }

        public int TotalParts { get; set; }
    }

    public class LargeUploadPartsRequest
    {
        [Required]
        public string Key { get; set; } = null!;

        [Required]
        public string UploadId { get; set; } = null!;

        [Required]
        [MinLength(1)]
        [MaxLength(200)]
        public List<int> PartNumbers { get; set; } = [];
    }

    public class LargeUploadPartUrlDto
    {
        public int PartNumber { get; set; }

        public string Url { get; set; } = null!;
    }

    public class LargeUploadPartDto
    {
        [Range(1, 10000)]
        public int PartNumber { get; set; }

        [Required]
        public string ETag { get; set; } = null!;
    }

    /// <summary>Finishes a browser-to-bucket upload and creates the Resource, with the same metadata as a normal upload.</summary>
    public class CompleteLargeUploadRequest : CreateResourceRequest
    {
        [Required]
        public string Key { get; set; } = null!;

        [Required]
        public string UploadId { get; set; } = null!;

        [MaxLength(100)]
        public string? ContentType { get; set; }

        [Required]
        [MinLength(1)]
        public List<LargeUploadPartDto> Parts { get; set; } = [];
    }

    public class AbortLargeUploadRequest
    {
        [Required]
        public string Key { get; set; } = null!;

        [Required]
        public string UploadId { get; set; } = null!;
    }

    /// <summary>A short-lived URL a parent's browser plays a shared recording from.</summary>
    public class ResourcePlaybackDto
    {
        public string Url { get; set; } = null!;

        public string? MimeType { get; set; }

        public DateTime ExpiresAtUtc { get; set; }
    }
}
