using iucs.readernest.application.Dto.Communication;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Services
{
    /// <summary>The "Ask a Doubt" chatbot: FAQ knowledge base, per-user chat history, and
    /// teacher escalation for questions it can't answer.</summary>
    public interface IChatbotService
    {
        Task<IReadOnlyList<ChatFaqDto>> ListActiveFaqsAsync(CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ChatFaqDto>> ListAllFaqsAsync(CancellationToken cancellationToken = default);

        Task<ChatFaqDto> CreateFaqAsync(SaveChatFaqRequest request, CancellationToken cancellationToken = default);

        Task<ChatFaqDto> UpdateFaqAsync(Guid id, SaveChatFaqRequest request, CancellationToken cancellationToken = default);

        Task DeleteFaqAsync(Guid id, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ChatMessageDto>> ListMyMessagesAsync(Guid userId, CancellationToken cancellationToken = default);

        Task<AskChatbotResponse> AskAsync(Guid userId, AskChatbotRequest request, CancellationToken cancellationToken = default);

        Task<ChatMessageDto> SubmitFeedbackAsync(
            Guid userId,
            Guid messageId,
            SubmitChatFeedbackRequest request,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ChatEscalationDto>> ListEscalationsAsync(ChatEscalationStatus? status, CancellationToken cancellationToken = default);

        Task<ChatEscalationDto> ResolveEscalationAsync(
            Guid id,
            Guid resolvedByUserId,
            ResolveChatEscalationRequest request,
            CancellationToken cancellationToken = default);

        Task<ChatbotUsageStatsDto> GetUsageStatsAsync(CancellationToken cancellationToken = default);
    }
}
