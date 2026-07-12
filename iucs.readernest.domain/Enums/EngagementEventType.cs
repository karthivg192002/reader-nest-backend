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
        /// <summary>Seconds this participant was the dominant speaker (talk-time analysis).</summary>
        TalkTimeSeconds,
        /// <summary>Seconds this participant kept their camera on (attentiveness signal).</summary>
        CameraOnSeconds,
    }
}
