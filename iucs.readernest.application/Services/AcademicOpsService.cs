using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Academics;
using iucs.readernest.domain.Entities.Academics;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class AcademicOpsService : IAcademicOpsService
    {
        private static readonly TimeSpan LeaveCutoff = TimeSpan.FromHours(6);

        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;
        private readonly INotificationService _notificationService;

        public AcademicOpsService(IUnitOfWork unitOfWork, IAuditLogService auditLog, INotificationService notificationService)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
            _notificationService = notificationService;
        }

        public async Task<IReadOnlyList<HolidayDto>> ListHolidaysAsync(CancellationToken cancellationToken = default)
        {
            var holidays = await _unitOfWork.Repository<Holiday>().Query()
                .OrderBy(h => h.Date)
                .ToListAsync(cancellationToken);
            return holidays.Select(ToDto).ToList();
        }

        public async Task<HolidayDto> CreateHolidayAsync(SaveHolidayRequest request, CancellationToken cancellationToken = default)
        {
            var exists = await _unitOfWork.Repository<Holiday>().ExistsAsync(h => h.Date == request.Date, cancellationToken);
            if (exists)
            {
                throw new ConflictException($"A holiday already exists on {request.Date:yyyy-MM-dd}.");
            }

            var holiday = new Holiday { Date = request.Date, Name = request.Name.Trim(), Description = request.Description };
            await _unitOfWork.Repository<Holiday>().AddAsync(holiday, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(Holiday), holiday.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(holiday);
        }

        public async Task DeleteHolidayAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var holiday = await _unitOfWork.Repository<Holiday>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(Holiday), id);

            _unitOfWork.Repository<Holiday>().Remove(holiday);
            await _auditLog.StageAsync(AuditAction.Delete, nameof(Holiday), id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        public async Task<LeaveRequestDto> SubmitLeaveAsync(
            Guid teacherUserId,
            SubmitLeaveRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.EndAtUtc <= request.StartAtUtc)
            {
                throw new DomainValidationException("Leave end time must be after the start time.");
            }

            var teacher = await _unitOfWork.Repository<TeacherProfile>().Query()
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.UserId == teacherUserId, cancellationToken)
                ?? throw new NotFoundException("No teacher profile is linked to the current account.");

            var affectedSessions = await CountAffectedSessionsAsync(teacher.Id, request.StartAtUtc, request.EndAtUtc, cancellationToken);

            // 6-hour rule: leave covering a session that starts within the cutoff is auto-blocked
            var cutoffLimit = DateTime.UtcNow.Add(LeaveCutoff);
            var blockingSession = await _unitOfWork.Repository<ClassSession>().Query()
                .Where(s => s.TeacherProfileId == teacher.Id
                            && s.Status == SessionStatus.Scheduled
                            && s.ScheduledStartAtUtc < cutoffLimit
                            && s.ScheduledStartAtUtc < request.EndAtUtc
                            && s.ScheduledEndAtUtc > request.StartAtUtc)
                .OrderBy(s => s.ScheduledStartAtUtc)
                .FirstOrDefaultAsync(cancellationToken);
            if (blockingSession is not null)
            {
                throw new DomainValidationException(
                    $"Leave cannot cover the session at {blockingSession.ScheduledStartAtUtc:u}: applications must be made at least 6 hours before a scheduled class.");
            }

            var leave = new LeaveRequest
            {
                TeacherProfileId = teacher.Id,
                StartAtUtc = request.StartAtUtc,
                EndAtUtc = request.EndAtUtc,
                Reason = request.Reason.Trim(),
            };
            await _unitOfWork.Repository<LeaveRequest>().AddAsync(leave, cancellationToken);
            await _auditLog.StageAsync(AuditAction.Create, nameof(LeaveRequest), leave.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            await NotifyAdminsAsync(
                "Teacher leave application",
                $"{teacher.User.FirstName} {teacher.User.LastName} applied for leave {request.StartAtUtc:u} – {request.EndAtUtc:u} " +
                $"({affectedSessions} scheduled session(s) affected). Reason: {request.Reason}",
                cancellationToken);

            leave.TeacherProfile = teacher;
            return await ToDtoAsync(leave, cancellationToken);
        }

        public async Task<IReadOnlyList<LeaveRequestDto>> ListLeaveAsync(
            LeaveStatus? status,
            CancellationToken cancellationToken = default)
        {
            IQueryable<LeaveRequest> query = _unitOfWork.Repository<LeaveRequest>().Query()
                .Include(l => l.TeacherProfile).ThenInclude(t => t.User);
            if (status.HasValue)
            {
                query = query.Where(l => l.Status == status.Value);
            }

            var leaves = await query.OrderByDescending(l => l.CreatedAtUtc).ToListAsync(cancellationToken);
            var result = new List<LeaveRequestDto>(leaves.Count);
            foreach (var leave in leaves)
            {
                result.Add(await ToDtoAsync(leave, cancellationToken));
            }

            return result;
        }

        public async Task<IReadOnlyList<LeaveRequestDto>> ListLeaveForTeacherUserAsync(
            Guid teacherUserId,
            CancellationToken cancellationToken = default)
        {
            var teacher = await _unitOfWork.Repository<TeacherProfile>()
                .FirstOrDefaultAsync(t => t.UserId == teacherUserId, cancellationToken)
                ?? throw new NotFoundException("No teacher profile is linked to the current account.");

            var leaves = await _unitOfWork.Repository<LeaveRequest>().Query()
                .Include(l => l.TeacherProfile).ThenInclude(t => t.User)
                .Where(l => l.TeacherProfileId == teacher.Id)
                .OrderByDescending(l => l.CreatedAtUtc)
                .ToListAsync(cancellationToken);

            var result = new List<LeaveRequestDto>(leaves.Count);
            foreach (var leave in leaves)
            {
                result.Add(await ToDtoAsync(leave, cancellationToken));
            }

            return result;
        }

        public async Task<LeaveRequestDto> ReviewLeaveAsync(
            Guid id,
            ReviewLeaveRequest request,
            CancellationToken cancellationToken = default)
        {
            var leave = await _unitOfWork.Repository<LeaveRequest>().Query()
                .Include(l => l.TeacherProfile).ThenInclude(t => t.User)
                .FirstOrDefaultAsync(l => l.Id == id, cancellationToken)
                ?? throw new NotFoundException(nameof(LeaveRequest), id);

            if (leave.Status != LeaveStatus.Pending)
            {
                throw new DomainValidationException($"This leave application is already {leave.Status}.");
            }

            leave.Status = request.Approve ? LeaveStatus.Approved : LeaveStatus.Rejected;
            leave.ReviewNote = request.ReviewNote;
            leave.ReviewedAtUtc = DateTime.UtcNow;

            await _auditLog.StageAsync(AuditAction.Update, nameof(LeaveRequest), leave.Id.ToString(), cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var teacherUser = leave.TeacherProfile.User;
            await _notificationService.SendEmailAsync(
                teacherUser.Id,
                teacherUser.Email,
                NotificationType.LeaveStatusUpdate,
                $"Leave application {leave.Status}",
                $"Your leave for {leave.StartAtUtc:u} – {leave.EndAtUtc:u} was {leave.Status}." +
                (string.IsNullOrEmpty(request.ReviewNote) ? "" : $" Note: {request.ReviewNote}"),
                cancellationToken);

            return await ToDtoAsync(leave, cancellationToken);
        }

        public async Task<IReadOnlyList<SessionAttendanceDto>> CaptureAttendanceAsync(
            Guid sessionId,
            CaptureAttendanceRequest request,
            CancellationToken cancellationToken = default)
        {
            var sessionExists = await _unitOfWork.Repository<ClassSession>()
                .ExistsAsync(s => s.Id == sessionId, cancellationToken);
            if (!sessionExists)
            {
                throw new NotFoundException(nameof(ClassSession), sessionId);
            }

            var repository = _unitOfWork.Repository<SessionAttendance>();
            foreach (var entry in request.Entries)
            {
                if ((entry.ChildId is null) == (entry.TeacherProfileId is null))
                {
                    throw new DomainValidationException("Each attendance entry must set exactly one of childId or teacherProfileId.");
                }

                // Rejoin after a network drop updates the existing row, never duplicates it
                var existing = await repository.FirstOrDefaultAsync(
                    a => a.ClassSessionId == sessionId
                         && a.ChildId == entry.ChildId
                         && a.TeacherProfileId == entry.TeacherProfileId,
                    cancellationToken);

                if (existing is null)
                {
                    await repository.AddAsync(
                        new SessionAttendance
                        {
                            ClassSessionId = sessionId,
                            ParticipantType = entry.ChildId is not null ? ParticipantType.Student : ParticipantType.Teacher,
                            ChildId = entry.ChildId,
                            TeacherProfileId = entry.TeacherProfileId,
                            Status = entry.Status,
                            JoinedAtUtc = entry.JoinedAtUtc,
                            LeftAtUtc = entry.LeftAtUtc,
                        },
                        cancellationToken);
                }
                else
                {
                    existing.Status = entry.Status;
                    existing.JoinedAtUtc = entry.JoinedAtUtc ?? existing.JoinedAtUtc;
                    existing.LeftAtUtc = entry.LeftAtUtc ?? existing.LeftAtUtc;
                }
            }

            await _auditLog.StageAsync(AuditAction.Update, nameof(SessionAttendance), sessionId.ToString(),
                changesJson: $"{{\"entries\":{request.Entries.Count}}}", cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            // Attendance updates: parents hear about an absence the moment it is recorded
            var absentChildIds = request.Entries
                .Where(e => e.ChildId.HasValue && e.Status == AttendanceStatus.Absent)
                .Select(e => e.ChildId!.Value)
                .ToList();
            if (absentChildIds.Count > 0)
            {
                var absentChildren = await _unitOfWork.Repository<Child>().Query()
                    .Include(c => c.ParentProfile).ThenInclude(p => p.User)
                    .Where(c => absentChildIds.Contains(c.Id))
                    .ToListAsync(cancellationToken);
                foreach (var child in absentChildren)
                {
                    var parentUser = child.ParentProfile.User;
                    await _notificationService.SendEmailAsync(
                        parentUser.Id,
                        parentUser.Email,
                        NotificationType.AttendanceUpdate,
                        $"{child.FirstName} was marked absent today",
                        $"{child.FirstName} missed today's class. If this was unplanned, please reach out so the session can be carried forward.",
                        cancellationToken);
                }
            }

            return await ListAttendanceAsync(sessionId, cancellationToken);
        }

        public async Task<IReadOnlyList<SessionAttendanceDto>> ListAttendanceAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            var rows = await _unitOfWork.Repository<SessionAttendance>().Query()
                .Include(a => a.Child)
                .Where(a => a.ClassSessionId == sessionId)
                .ToListAsync(cancellationToken);

            return rows.Select(a => new SessionAttendanceDto
            {
                Id = a.Id,
                ClassSessionId = a.ClassSessionId,
                ParticipantType = a.ParticipantType,
                ChildId = a.ChildId,
                ChildName = a.Child is null ? null : $"{a.Child.FirstName} {a.Child.LastName}",
                TeacherProfileId = a.TeacherProfileId,
                Status = a.Status,
                JoinedAtUtc = a.JoinedAtUtc,
                LeftAtUtc = a.LeftAtUtc,
            }).ToList();
        }

        private async Task<int> CountAffectedSessionsAsync(
            Guid teacherProfileId,
            DateTime startUtc,
            DateTime endUtc,
            CancellationToken cancellationToken)
        {
            return await _unitOfWork.Repository<ClassSession>().Query()
                .CountAsync(
                    s => s.TeacherProfileId == teacherProfileId
                         && s.Status == SessionStatus.Scheduled
                         && s.ScheduledStartAtUtc < endUtc
                         && s.ScheduledEndAtUtc > startUtc,
                    cancellationToken);
        }

        private async Task NotifyAdminsAsync(string subject, string body, CancellationToken cancellationToken)
        {
            var admins = await _unitOfWork.Repository<User>().Query()
                .Where(u => u.Role == UserRole.Admin && u.Status == UserStatus.Active)
                .ToListAsync(cancellationToken);
            foreach (var admin in admins)
            {
                await _notificationService.SendEmailAsync(
                    admin.Id, admin.Email, NotificationType.General, subject, body, cancellationToken);
            }
        }

        private async Task<LeaveRequestDto> ToDtoAsync(LeaveRequest leave, CancellationToken cancellationToken)
        {
            return new LeaveRequestDto
            {
                Id = leave.Id,
                TeacherProfileId = leave.TeacherProfileId,
                TeacherName = $"{leave.TeacherProfile.User.FirstName} {leave.TeacherProfile.User.LastName}",
                StartAtUtc = leave.StartAtUtc,
                EndAtUtc = leave.EndAtUtc,
                Reason = leave.Reason,
                Status = leave.Status,
                ReviewNote = leave.ReviewNote,
                CreatedAtUtc = leave.CreatedAtUtc,
                AffectedSessionCount = await CountAffectedSessionsAsync(
                    leave.TeacherProfileId, leave.StartAtUtc, leave.EndAtUtc, cancellationToken),
            };
        }

        private static HolidayDto ToDto(Holiday holiday)
        {
            return new HolidayDto
            {
                Id = holiday.Id,
                Date = holiday.Date,
                Name = holiday.Name,
                Description = holiday.Description,
            };
        }
    }
}
