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
        /// <summary>
        /// No longer written -- the screen-share-nudge/whiteboard-capture feature was removed.
        /// Kept only because EngagementEvent.Type is stored as a string (see
        /// ReaderNestDbContext's EnumToStringConverter): deleting this member would throw when
        /// EF deserializes any row already persisted with this value.
        /// </summary>
        ScreenShareSeconds,
    }
}
