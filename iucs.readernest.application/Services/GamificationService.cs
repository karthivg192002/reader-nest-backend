using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Sessions;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Entities.Users;
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
        private readonly ISessionService _sessionService;

        public GamificationService(IUnitOfWork unitOfWork, ISessionService sessionService)
        {
            _unitOfWork = unitOfWork;
            _sessionService = sessionService;
        }

        public async Task<IReadOnlyList<AwardDto>> GrantAsync(
            Guid callerUserId, GrantAwardRequest request, CancellationToken cancellationToken = default)
        {
            // Milestones are exclusively server-computed below when stars cross a
            // threshold — a client must never be able to mint one directly.
            if (request.Kind == AwardKind.Milestone)
            {
                throw new DomainValidationException("Milestone awards are granted automatically and cannot be requested directly.");
            }

            var caller = await _unitOfWork.Repository<User>().GetByIdAsync(callerUserId, cancellationToken)
                ?? throw new UnauthorizedException("Unknown user.");
            var isStaff = caller.Role is UserRole.Admin or UserRole.Teacher;

            // A named Badge is "granted by a teacher or an activity" (see AwardKind) — never
            // self-awarded by a student/parent. A Star is the live-quiz self-report flow any
            // genuine session participant can post for themselves.
            if (request.Kind == AwardKind.Badge && !isStaff)
            {
                throw new ForbiddenException("Only a teacher or admin can grant a badge.");
            }

            if (request.SessionId.HasValue)
            {
                if (!await _sessionService.IsSessionParticipantAsync(request.SessionId.Value, callerUserId, cancellationToken))
                {
                    throw new ForbiddenException("You do not have access to this session.");
                }
            }
            else if (!isStaff)
            {
                // No session to scope against — only a trusted role can award outside a
                // specific, ownership-checked class.
                throw new ForbiddenException("An award not tied to a specific session can only be granted by a teacher or admin.");
            }

            var name = request.ParticipantName.Trim();

            // The self-report Star flow (a parent's child answering their own live quiz) took
            // ParticipantName as free text with no check it was actually the caller's own
            // child — any session participant could post a Star under a classmate's name (or
            // any name at all), inflating that name's persisted award history and leaderboard
            // rank. Scoped to Parent + Star + session-scoped specifically: that's the only
            // combination reachable through the self-report path (staff-granted badges/stars
            // legitimately name a student who isn't the caller, and IsSessionParticipantAsync's
            // own Parent branch already guarantees session.BatchId is set for this caller to
            // have gotten this far, so there's always a batch to check enrollment against).
            if (caller.Role == UserRole.Parent && request.Kind == AwardKind.Star && request.SessionId.HasValue)
            {
                var session = await _unitOfWork.Repository<ClassSession>().GetByIdAsync(request.SessionId.Value, cancellationToken);
                var ownChildNames = session?.BatchId is Guid batchId
                    ? await _unitOfWork.Repository<Child>().Query()
                        .Where(c => c.ParentProfile.UserId == callerUserId)
                        .Join(
                            _unitOfWork.Repository<BatchEnrollment>().Query().Where(e => e.BatchId == batchId),
                            c => c.Id,
                            e => e.ChildId,
                            (c, _) => c.FirstName + " " + c.LastName)
                        .ToListAsync(cancellationToken)
                    : [];

                if (!ownChildNames.Any(n => n.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)))
                {
                    throw new ForbiddenException("You can only report a star for your own child in this class.");
                }
            }
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
