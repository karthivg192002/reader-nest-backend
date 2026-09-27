using iucs.readernest.application.Dto.Communication;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Services
{
    /// <summary>
    /// Parent support tickets: parents raise and follow up on concerns from the portal;
    /// Relationship Managers and Admin (SupportTickets module) answer and resolve them.
    /// </summary>
    public interface ISupportTicketService
    {
        /// <summary>The parent's own tickets, most recently active first.</summary>
        Task<IReadOnlyList<SupportTicketSummaryDto>> ListForParentAsync(Guid parentUserId, CancellationToken cancellationToken = default);

        /// <summary>One of the parent's own tickets with its thread; 404s for anyone else's.</summary>
        Task<SupportTicketDetailDto> GetForParentAsync(Guid parentUserId, Guid ticketId, CancellationToken cancellationToken = default);

        /// <summary>Opens a ticket with its first message and alerts every RM/Admin.</summary>
        Task<SupportTicketDetailDto> CreateAsync(Guid parentUserId, CreateSupportTicketRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Adds the parent's message. Replying on a Resolved/Closed ticket reopens it, so a
        /// family never has to start over when the fix didn't hold.
        /// </summary>
        Task<SupportTicketDetailDto> ParentReplyAsync(Guid parentUserId, Guid ticketId, ReplySupportTicketRequest request, CancellationToken cancellationToken = default);

        /// <summary>The staff queue, optionally one status only / matching a search term, most recently active first.</summary>
        Task<IReadOnlyList<SupportTicketSummaryDto>> ListForStaffAsync(SupportTicketStatus? status, string? search, CancellationToken cancellationToken = default);

        Task<SupportTicketCountsDto> GetCountsAsync(CancellationToken cancellationToken = default);

        Task<SupportTicketDetailDto> GetForStaffAsync(Guid ticketId, CancellationToken cancellationToken = default);

        /// <summary>Adds a team reply (an Open ticket moves to In Progress) and emails the parent.</summary>
        Task<SupportTicketDetailDto> StaffReplyAsync(Guid staffUserId, Guid ticketId, ReplySupportTicketRequest request, CancellationToken cancellationToken = default);

        Task<SupportTicketDetailDto> UpdateStatusAsync(Guid staffUserId, Guid ticketId, SupportTicketStatus status, CancellationToken cancellationToken = default);
    }
}
