using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Users
{
    public class AccessRequestDto
    {
        public Guid Id { get; set; }

        public Guid RequestedByUserId { get; set; }

        public string RequestedByName { get; set; } = null!;

        public string RequestedByEmail { get; set; } = null!;

        public IReadOnlyList<string> RequestedModules { get; set; } = [];

        public AccessRequestStatus Status { get; set; }

        public Guid? ReviewedBy { get; set; }

        public string? ReviewedByName { get; set; }

        public DateTime? ReviewedAtUtc { get; set; }

        public string? ReviewNote { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }

    public class SubmitAccessRequestRequest
    {
        /// <summary>The modules the Sub Admin doesn't currently hold and is asking for.</summary>
        [Required]
        [MinLength(1)]
        public List<string> RequestedModules { get; set; } = [];
    }

    public class ReviewAccessRequestRequest
    {
        [Required]
        public bool Approve { get; set; }

        [MaxLength(500)]
        public string? ReviewNote { get; set; }
    }
}
