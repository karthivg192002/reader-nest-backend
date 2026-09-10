namespace iucs.readernest.application.Dto.Sessions
{
    public class SessionPresentationDto
    {
        public Guid Id { get; set; }

        public Guid ClassSessionId { get; set; }

        public string OriginalFileName { get; set; } = null!;

        public DateTime CreatedAtUtc { get; set; }
    }

    /// <summary>Backend-internal shape a download action needs — the DTO above deliberately
    /// never exposes the raw storage path.</summary>
    public class SessionPresentationDownloadDto
    {
        public string StorageUrl { get; set; } = null!;

        public string OriginalFileName { get; set; } = null!;
    }
}
