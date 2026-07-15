using iucs.readernest.application.Dto.Sessions;

namespace iucs.readernest.application.Services
{
    public interface IGamificationService
    {
        /// <summary>
        /// Persists an award; star grants auto-create milestone awards when the
        /// participant's session stars cross 3/6/10.
        /// </summary>
        Task<IReadOnlyList<AwardDto>> GrantAsync(GrantAwardRequest request, CancellationToken cancellationToken = default);

        /// <summary>Aggregated stars + badges per participant — session-scoped or all-time.</summary>
        Task<IReadOnlyList<LeaderboardEntryDto>> GetLeaderboardAsync(Guid? sessionId, int top, CancellationToken cancellationToken = default);
    }
}
