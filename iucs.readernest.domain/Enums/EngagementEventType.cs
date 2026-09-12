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
        /// Seconds this participant (in practice, always the teacher) had their screen shared.
        /// Jibri (the server-side recording bot) only ever records this room's own video/stage
        /// view, never the app's custom whiteboard/quiz overlay -- screen-sharing the class tab
        /// is what actually gets the overlay into the recording (see JitsiLive.tsx's
        /// screen-share nudge banner). This is a durable "was it actually captured" signal, not
        /// a factor in EngagementScoring -- checked at recording-review time, not scored.
        /// </summary>
        ScreenShareSeconds,
    }
}
