using iucs.readernest.application.Dto.Sessions;
using iucs.readernest.application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// Persistent gamification: stars/badges/milestones earned in live classes.
    /// Any signed-in participant can post their own awards from the classroom;
    /// the leaderboard is readable by every portal (names only, no PII).
    /// </summary>
    [ApiController]
    [Route("api/gamification")]
    [Authorize]
    public class GamificationController : ControllerBase
    {
        private readonly IGamificationService _gamificationService;

        public GamificationController(IGamificationService gamificationService)
        {
            _gamificationService = gamificationService;
        }

        [HttpPost("awards")]
        public async Task<ActionResult<IReadOnlyList<AwardDto>>> Grant(
            GrantAwardRequest request,
            CancellationToken cancellationToken)
        {
            return Ok(await _gamificationService.GrantAsync(request, cancellationToken));
        }

        [HttpGet("leaderboard")]
        public async Task<ActionResult<IReadOnlyList<LeaderboardEntryDto>>> Leaderboard(
            [FromQuery] Guid? sessionId,
            [FromQuery] int top = 10,
            CancellationToken cancellationToken = default)
        {
            return Ok(await _gamificationService.GetLeaderboardAsync(sessionId, top, cancellationToken));
        }
    }
}
