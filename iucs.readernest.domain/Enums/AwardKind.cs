namespace iucs.readernest.domain.Enums
{
    /// <summary>Gamification award types persisted per student.</summary>
    public enum AwardKind
    {
        /// <summary>One star, typically for a correct quiz answer.</summary>
        Star,

        /// <summary>A named badge granted by a teacher or an activity.</summary>
        Badge,

        /// <summary>Auto-granted when session stars cross a threshold (3/6/10).</summary>
        Milestone
    }
}
