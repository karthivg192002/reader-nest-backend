using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Communication
{
    /// <summary>A ticket row for the parent's list and the Relationship Manager queue (no thread).</summary>
    public class SupportTicketSummaryDto
    {
        public Guid Id { get; set; }

        /// <summary>Short human reference, e.g. "T-3F9A21C0", for quoting in conversation.</summary>
        public string Reference { get; set; } = null!;

        public string Subject { get; set; } = null!;

        public SupportTicketCategory Category { get; set; }

        public SupportTicketStatus Status { get; set; }

        public Guid ParentUserId { get; set; }

        public string ParentName { get; set; } = null!;

        public string ParentEmail { get; set; } = null!;

        public string? ParentPhone { get; set; }

        public Guid? ChildId { get; set; }

        public string? ChildName { get; set; }

        /// <summary>The parent wrote last, so the ticket is waiting on the team.</summary>
        public bool AwaitingStaffReply { get; set; }

        public int MessageCount { get; set; }

        /// <summary>First ~140 characters of the newest message.</summary>
        public string? LastMessagePreview { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime LastActivityAtUtc { get; set; }

        public DateTime? ResolvedAtUtc { get; set; }
    }

    public class SupportTicketMessageDto
    {
        public Guid Id { get; set; }

        public string AuthorName { get; set; } = null!;

        public bool IsStaff { get; set; }

        public string Body { get; set; } = null!;

        public DateTime CreatedAtUtc { get; set; }
    }

    public class SupportTicketDetailDto : SupportTicketSummaryDto
    {
        public IReadOnlyList<SupportTicketMessageDto> Messages { get; set; } = [];
    }

    public class CreateSupportTicketRequest
    {
        [Required]
        public SupportTicketCategory Category { get; set; }

        [Required, MaxLength(200)]
        public string Subject { get; set; } = string.Empty;

        [Required, MaxLength(4000)]
        public string Message { get; set; } = string.Empty;

        /// <summary>Optional; must be one of the signed-in parent's own children.</summary>
        public Guid? ChildId { get; set; }
    }

    public class ReplySupportTicketRequest
    {
        [Required, MaxLength(4000)]
        public string Message { get; set; } = string.Empty;
    }

    public class UpdateSupportTicketStatusRequest
    {
        [Required]
        public SupportTicketStatus Status { get; set; }
    }

    /// <summary>Counts for the RM queue's filter tabs and dashboard badge.</summary>
    public class SupportTicketCountsDto
    {
        public int Open { get; set; }

        public int InProgress { get; set; }

        public int Resolved { get; set; }

        public int Closed { get; set; }

        /// <summary>Open or In Progress tickets whose last message is from the parent.</summary>
        public int AwaitingReply { get; set; }
    }
}
