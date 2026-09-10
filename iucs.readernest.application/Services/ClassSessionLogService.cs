using iucs.readernest.application.Dto.Common;
using iucs.readernest.application.Dto.Sessions;
using iucs.readernest.application.Mappings;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class ClassSessionLogService : IClassSessionLogService
    {
        // How far ahead "next classes" looks and how many it shows on the dashboard.
        private const int NextClassesCount = 10;

        private readonly IUnitOfWork _unitOfWork;

        public ClassSessionLogService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task<ClassSessionLogDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var todayStartUtc = now.Date;
            var weekStartUtc = now.AddDays(-7);

            var live = await SessionBaseQuery()
                .Where(s => s.Status == SessionStatus.InProgress)
                .OrderByDescending(s => s.ActualStartAtUtc ?? s.ScheduledStartAtUtc)
                .ToListAsync(cancellationToken);

            var next = await SessionBaseQuery()
                .Where(s => (s.Status == SessionStatus.Scheduled || s.Status == SessionStatus.CarriedForward)
                    && s.ScheduledStartAtUtc >= now)
                .OrderBy(s => s.ScheduledStartAtUtc)
                .Take(NextClassesCount)
                .ToListAsync(cancellationToken);

            var unexpectedToday = await _unitOfWork.Repository<ClassSessionEventLog>().Query()
                .CountAsync(e => !e.IsExpected && e.OccurredAtUtc >= todayStartUtc, cancellationToken);

            var noShowsThisWeek = await _unitOfWork.Repository<ClassSessionEventLog>().Query()
                .CountAsync(e => e.OccurredAtUtc >= weekStartUtc
                    && (e.EventType == ClassSessionEventType.TeacherNoShow || e.EventType == ClassSessionEventType.StudentNoShow),
                    cancellationToken);

            // Today's status breakdown — every session whose scheduled window touches today,
            // same idiom used across the app for a session's local-timezone "day".
            var todayEndUtc = todayStartUtc.AddDays(1);
            var statusCounts = await _unitOfWork.Repository<ClassSession>().Query()
                .Where(s => s.ScheduledStartAtUtc < todayEndUtc && s.ScheduledEndAtUtc >= todayStartUtc)
                .GroupBy(s => s.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync(cancellationToken);

            return new ClassSessionLogDashboardDto
            {
                LiveClasses = live.Select(s => s.ToDto()).ToList(),
                NextClasses = next.Select(s => s.ToDto()).ToList(),
                UnexpectedEventsToday = unexpectedToday,
                NoShowsThisWeek = noShowsThisWeek,
                StatusCounts = statusCounts.ToDictionary(x => x.Status.ToString(), x => x.Count),
            };
        }

        public async Task<PagedResult<ClassSessionEventLogDto>> ListEventLogsAsync(
            DateTime? fromUtc,
            DateTime? toUtc,
            Guid? sessionId,
            Guid? teacherProfileId,
            ClassSessionEventType? eventType,
            bool? expectedOnly,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, 200);

            var query = _unitOfWork.Repository<ClassSessionEventLog>().Query();

            if (fromUtc.HasValue)
            {
                query = query.Where(e => e.OccurredAtUtc >= fromUtc.Value);
            }
            if (toUtc.HasValue)
            {
                query = query.Where(e => e.OccurredAtUtc <= toUtc.Value);
            }
            if (sessionId.HasValue)
            {
                query = query.Where(e => e.ClassSessionId == sessionId.Value);
            }
            if (eventType.HasValue)
            {
                query = query.Where(e => e.EventType == eventType.Value);
            }
            if (expectedOnly.HasValue)
            {
                query = query.Where(e => e.IsExpected == expectedOnly.Value);
            }
            if (teacherProfileId.HasValue)
            {
                // A teacher's own footprint spans every role a row can carry them under
                // (their own start/leave rows carry TeacherProfileId directly) plus every
                // other event logged against their session (a student's join/leave, a
                // no-show, a denial) — join back to the session to catch those too.
                query = query.Where(e =>
                    e.TeacherProfileId == teacherProfileId.Value
                    || e.ClassSession.TeacherProfileId == teacherProfileId.Value);
            }

            var totalCount = await query.CountAsync(cancellationToken);

            // Projected inline (not via a shared helper method) — EF Core's SQL translator
            // has to see the member-init expression directly in the Select lambda; a call out
            // to a separate static method here silently fails to translate at *query
            // execution* time (not at compile time), surfacing as a bare 500 on this endpoint
            // while GetDashboardAsync's own (non-projecting, ToDto()-after-materialize) query
            // kept working fine. Confirmed live: the dashboard loaded real counts while this
            // endpoint 500'd "An unexpected error occurred." on every request. Same fix
            // applied to GetSessionTimelineAsync below.
            var rows = await query
                .OrderByDescending(e => e.OccurredAtUtc)
                .ThenBy(e => e.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(e => new ClassSessionEventLogDto
                {
                    Id = e.Id,
                    ClassSessionId = e.ClassSessionId,
                    EventType = e.EventType,
                    ParticipantType = e.ParticipantType,
                    ParticipantName = e.ParticipantName,
                    OccurredAtUtc = e.OccurredAtUtc,
                    IsExpected = e.IsExpected,
                    Detail = e.Detail,
                    SessionType = e.ClassSession.Type,
                    TeacherName = e.ClassSession.TeacherProfile.User.FirstName + " " + e.ClassSession.TeacherProfile.User.LastName,
                    BatchName = e.ClassSession.Batch != null ? e.ClassSession.Batch.Name : null,
                    ScheduledStartAtUtc = e.ClassSession.ScheduledStartAtUtc,
                })
                .ToListAsync(cancellationToken);

            return new PagedResult<ClassSessionEventLogDto>
            {
                Items = rows,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
            };
        }

        public async Task<IReadOnlyList<ClassSessionEventLogDto>> GetSessionTimelineAsync(
            Guid sessionId, CancellationToken cancellationToken = default)
        {
            return await _unitOfWork.Repository<ClassSessionEventLog>().Query()
                .Where(e => e.ClassSessionId == sessionId)
                .OrderBy(e => e.OccurredAtUtc)
                .Select(e => new ClassSessionEventLogDto
                {
                    Id = e.Id,
                    ClassSessionId = e.ClassSessionId,
                    EventType = e.EventType,
                    ParticipantType = e.ParticipantType,
                    ParticipantName = e.ParticipantName,
                    OccurredAtUtc = e.OccurredAtUtc,
                    IsExpected = e.IsExpected,
                    Detail = e.Detail,
                    SessionType = e.ClassSession.Type,
                    TeacherName = e.ClassSession.TeacherProfile.User.FirstName + " " + e.ClassSession.TeacherProfile.User.LastName,
                    BatchName = e.ClassSession.Batch != null ? e.ClassSession.Batch.Name : null,
                    ScheduledStartAtUtc = e.ClassSession.ScheduledStartAtUtc,
                })
                .ToListAsync(cancellationToken);
        }

        private IQueryable<ClassSession> SessionBaseQuery()
        {
            return _unitOfWork.Repository<ClassSession>().Query()
                .Include(s => s.Batch)
                .Include(s => s.TeacherProfile).ThenInclude(t => t.User);
        }
    }
}
