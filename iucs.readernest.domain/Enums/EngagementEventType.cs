namespace iucs.readernest.domain.Enums
{
    /// <summary>Signals feeding the engagement score; weights live in the reports service.</summary>
    public enum EngagementEventType
    {
        QuizAttempt,
        QuizCorrect,
        ActivityClick,
        ActivityCompleted,
        WhiteboardInteraction,
        HandRaise,
        AttentionPing,
    }
}
