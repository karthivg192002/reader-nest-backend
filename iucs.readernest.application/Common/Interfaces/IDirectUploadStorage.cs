namespace iucs.readernest.application.Common.Interfaces
{
    public class DirectUploadSession
    {
        /// <summary>The object key, also the value stored as Resource.FileUrl.</summary>
        public string Key { get; set; } = null!;

        public string UploadId { get; set; } = null!;

        /// <summary>Every part except the last must be exactly this many bytes.</summary>
        public long PartSizeBytes { get; set; }
    }

    public class PresignedPart
    {
        public int PartNumber { get; set; }

        public string Url { get; set; } = null!;
    }

    public class UploadedPart
    {
        public int PartNumber { get; set; }

        public string ETag { get; set; } = null!;
    }

    /// <summary>
    /// Large-file storage where the browser sends the bytes straight to the object store in parts
    /// (multipart upload with short-lived presigned URLs) and later plays them back through a
    /// presigned read URL. The API never carries the video, so multi-GB recordings neither hit its
    /// request-size limit nor sit in its memory. Only an S3-compatible store can do this.
    /// </summary>
    public interface IDirectUploadStorage
    {
        /// <summary>Starts a multipart upload for a new object (validates the file type).</summary>
        Task<DirectUploadSession> StartMultipartAsync(string originalFileName, string? contentType, CancellationToken cancellationToken = default);

        /// <summary>Short-lived URLs the browser PUTs each part to.</summary>
        IReadOnlyList<PresignedPart> GetPartUrls(string key, string uploadId, IEnumerable<int> partNumbers);

        /// <summary>Stitches the parts into the object and returns its final size in bytes.</summary>
        Task<long> CompleteMultipartAsync(string key, string uploadId, IReadOnlyList<UploadedPart> parts, CancellationToken cancellationToken = default);

        /// <summary>Discards an unfinished upload and its stored parts.</summary>
        Task AbortMultipartAsync(string key, string uploadId, CancellationToken cancellationToken = default);

        /// <summary>A time-limited URL the browser can stream from (supports seeking), shown inline.</summary>
        string GetReadUrl(string key, TimeSpan validFor, string? contentType);
    }
}
