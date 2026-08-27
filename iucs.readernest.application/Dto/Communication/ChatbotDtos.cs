namespace iucs.readernest.application.Dto.Communication
{
    public class ChatFaqDto
    {
        public Guid Id { get; set; }

        public string Question { get; set; } = string.Empty;

        public string Answer { get; set; } = string.Empty;

        public string? Keywords { get; set; }

        public string? Category { get; set; }

        public bool IsActive { get; set; }

        public int SortOrder { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime? UpdatedAtUtc { get; set; }
    }

    public class SaveChatFaqRequest
    {
        public string Question { get; set; } = string.Empty;

        public string Answer { get; set; } = string.Empty;

        public string? Keywords { get; set; }

        public string? Category { get; set; }

        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; }
    }

    public class ChatMessageDto
    {
        public Guid Id { get; set; }

        /// <summary>"User" or "Bot".</summary>
        public string Sender { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        public Guid? MatchedFaqId { get; set; }

        /// <summary>Null until rated; only meaningful on a Bot message that matched an FAQ.</summary>
        public bool? WasHelpful { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }

    public class AskChatbotRequest
    {
        public string Message { get; set; } = string.Empty;
    }

    public class SubmitChatFeedbackRequest
    {
        public bool Helpful { get; set; }

        /// <summary>
        /// The question that produced this answer — the service has no other way to recover it
        /// for the escalation it creates on negative feedback, since a ChatMessage row only
        /// stores its own turn, not the one before it. The client always has this already (it's
        /// the immediately preceding turn in the conversation it's rendering).
        /// </summary>
        public string OriginalQuestion { get; set; } = string.Empty;
    }

    public class AskChatbotResponse
    {
        public ChatMessageDto UserMessage { get; set; } = null!;

        public ChatMessageDto BotMessage { get; set; } = null!;

        public bool Matched { get; set; }

        public bool Escalated { get; set; }
    }

    public class ChatEscalationDto
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public string UserName { get; set; } = string.Empty;

        /// <summary>"Pending" or "Resolved".</summary>
        public string Status { get; set; } = string.Empty;

        public string Question { get; set; } = string.Empty;

        public string? ResolutionNote { get; set; }

        public Guid? ResolvedByUserId { get; set; }

        public string? ResolvedByName { get; set; }

        public DateTime? ResolvedAtUtc { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }

    public class ResolveChatEscalationRequest
    {
        public string ResolutionNote { get; set; } = string.Empty;
    }

    public class ChatbotUsageStatsDto
    {
        public int TotalQuestions { get; set; }

        public int AnsweredByBot { get; set; }

        public int EscalatedToTeacher { get; set; }

        public int PendingEscalations { get; set; }

        public int ActiveUsers { get; set; }

        /// <summary>Bot answers a user explicitly marked unhelpful — a matched FAQ isn't
        /// necessarily a good answer, so this tracks quality separately from match rate.</summary>
        public int MarkedUnhelpful { get; set; }

        public IReadOnlyList<string> TopUnansweredQuestions { get; set; } = Array.Empty<string>();
    }
}
