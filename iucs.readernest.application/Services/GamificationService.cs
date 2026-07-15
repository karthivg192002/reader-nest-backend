using iucs.readernest.application.Dto.Sessions;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class GamificationService : IGamificationService
    {
        // Session-star thresholds that auto-grant a milestone (mirrors the live overlay)
        private static readonly (int Stars, string Label)[] Milestones =
        [
            (3, "Rising Star — 3 stars"),
            (6, "Star Reader — 6 stars"),
            (10, "Champion — 10 stars"),
        ];

        private readonly IUnitOfWork _unitOfWork;

        public GamificationService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<IReadOnlyList<AwardDto>> GrantAsync(GrantAwardRequest request, CancellationToken cancellationToken = default)
        {
            var name = request.ParticipantName.Trim();
            var repository = _unitOfWork.Repository<StudentAward>();
            var granted = new List<StudentAward>();

            var award = new StudentAward
            {
                ClassSessionId = request.SessionId,
                ParticipantName = name,
                Kind = request.Kind,
                Label = request.Label,
                Points = request.Points,
            };
            await repository.AddAsync(award, cancellationToken);
            granted.Add(award);

            if (request.Kind == AwardKind.Star && request.SessionId.HasValue)
            {
                var previousStars = await repository.Query()
                    .Where(a => a.ClassSessionId == request.SessionId
                        && a.ParticipantName == name
                        && a.Kind == AwardKind.Star)
                    .SumAsync(a => a.Points, cancellationToken);
                var totalStars = previousStars + request.Points;

                // Grant every milestone the new total crosses (a multi-point award can cross more than one)
                foreach (var (stars, label) in Milestones)
                {
                    if (previousStars < stars && totalStars >= stars)
                    {
                        var milestone = new StudentAward
                        {
                            ClassSessionId = request.SessionId,
                            ParticipantName = name,
                            Kind = AwardKind.Milestone,
                            Label = label,
                            Points = 0,
                        };
                        await repository.AddAsync(milestone, cancellationToken);
                        granted.Add(milestone);
                    }
                }
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return granted.Select(ToDto).ToList();
        }

        public async Task<IReadOnlyList<LeaderboardEntryDto>> GetLeaderboardAsync(
            Guid? sessionId,
            int top,
            CancellationToken cancellationToken = default)
        {
            var query = _unitOfWork.Repository<StudentAward>().Query();
            if (sessionId.HasValue)
            {
                query = query.Where(a => a.ClassSessionId == sessionId.Value);
            }

            var awards = await query.ToListAsync(cancellationToken);
            return awards
                .GroupBy(a => a.ParticipantName)
                .Select(g => new LeaderboardEntryDto
                {
                    ParticipantName = g.Key,
                    Stars = g.Where(a => a.Kind == AwardKind.Star).Sum(a => a.Points),
                    Badges = g.Where(a => a.Kind != AwardKind.Star && a.Label != null)
                        .OrderBy(a => a.CreatedAtUtc)
                        .Select(a => a.Label!)
                        .Distinct()
                        .ToList(),
                })
                .OrderByDescending(e => e.Stars)
                .Take(Math.Clamp(top, 1, 100))
                .ToList();
        }

        private static AwardDto ToDto(StudentAward award) => new()
        {
            Id = award.Id,
            SessionId = award.ClassSessionId,
            ParticipantName = award.ParticipantName,
            Kind = award.Kind,
            Label = award.Label,
            Points = award.Points,
            CreatedAtUtc = award.CreatedAtUtc,
        };
    }
}
