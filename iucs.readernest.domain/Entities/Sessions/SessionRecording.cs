using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Sessions
{
    /// <summary>
    /// Auto-recorded session stored in cloud storage. Parent access is view-only and
    /// expires 15 days after the batch finishes; a background job clears expired
    /// recordings from parent dashboards using ExpiresAtUtc.
    /// </summary>
    [Index(nameof(ExpiresAtUtc))]
    public class SessionRecording : BaseEntity
    {
        public Guid ClassSessionId { get; set; }

        public ClassSession ClassSession { get; set; } = null!;

        [MaxLength(1000)]
        public string StorageUrl { get; set; } = null!;

        public int? DurationSeconds { get; set; }

        public DateTime? ExpiresAtUtc { get; set; }
    }
}
