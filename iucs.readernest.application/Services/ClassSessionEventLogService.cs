using iucs.readernest.application.Dto.Sessions;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class ClassSessionEventLogService : IClassSessionEventLogService
    {
        // How late a teacher can start (or a student can join) after the scheduled time
        // before it's flagged as an unexpected scenario rather than an ordinary late arrival.
        private static readonly TimeSpan LateGrace = TimeSpan.FromMinutes(15);

        private readonly IUnitOfWork _unitOfWork;

        public ClassSessionEventLogService(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }

        public async Task LogTeacherJoinAsync(
            ClassSession session, Guid teacherProfileId, Guid? userId, string participantName,
            bool isReconnect, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            bool isExpected = true;
            string? detail = null;

            if (!isReconnect)
            {
                var lateBy = now - session.ScheduledStartAtUtc;
                if (lateBy > LateGrace)
                {
                    isExpected = false;
                    detail = $"Started {Math.Round(lateBy.TotalMinutes)} min after the scheduled time.";
                }

                if (session.Status is not (SessionStatus.Scheduled or SessionStatus.CarriedForward or SessionStatus.InProgress))
                {
                    isExpected = false;
                    detail = $"Joined while the session's status was '{session.Status}'.";
                }
            }

            await WriteAsync(new ClassSessionEventLog
            {
                ClassSessionId = session.Id,
                EventType = isReconnect ? ClassSessionEventType.TeacherJoined : ClassSessionEventType.TeacherStarted,
                ParticipantType = ParticipantType.Teacher,
                TeacherProfileId = teacherProfileId,
                UserId = userId,
                ParticipantName = participantName,
                OccurredAtUtc = now,
                IsExpected = isExpected,
                Detail = detail,
            }, cancellationToken);
        }

        public async Task LogStudentJoinAsync(
            ClassSession session, Guid childId, Guid? userId, string participantName,
            bool isReconnect, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;
            var isExpected = now <= session.ScheduledEndAtUtc;

            await WriteAsync(new ClassSessionEventLog
            {
                ClassSessionId = session.Id,
                EventType = isReconnect ? ClassSessionEventType.StudentRejoined : ClassSessionEventType.StudentJoined,
                ParticipantType = ParticipantType.Student,
                ChildId = childId,
                UserId = userId,
                ParticipantName = participantName,
                OccurredAtUtc = now,
                IsExpected = isExpected,
                Detail = isExpected ? null : "Joined after the class's scheduled end time.",
            }, cancellationToken);
        }

        public async Task LogDemoParticipantJoinAsync(
            Guid classSessionId, string participantName, CancellationToken cancellationToken = default)
        {
            await WriteAsync(new ClassSessionEventLog
            {
                ClassSessionId = classSessionId,
                EventType = ClassSessionEventType.DemoParticipantJoined,
                ParticipantName = participantName,
                OccurredAtUtc = DateTime.UtcNow,
                IsExpected = true,
            }, cancellationToken);
        }

        public async Task LogLeaveAsync(
            Guid classSessionId, ParticipantType participantType, Guid? teacherProfileId, Guid? childId,
            Guid? userId, string participantName, bool wasExplicit, CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;

            // No-tracking projection: the hub (the only caller today) never loads the full
            // entity for its own purposes, and this write must never fail because a session
            // somehow vanished between the room existing and this write landing.
            var sessionInfo = await _unitOfWork.Repository<ClassSession>().Query()
                .Where(s => s.Id == classSessionId)
                .Select(s => new { s.Status, s.ScheduledEndAtUtc })
                .FirstOrDefaultAsync(cancellationToken);
            if (sessionInfo is null)
            {
                return;
            }

            var classAlreadyOver = sessionInfo.Status == SessionStatus.Completed || now >= sessionInfo.ScheduledEndAtUtc;

            ClassSessionEventType eventType;
            bool isExpected;
            string? detail;

            if (wasExplicit)
            {
                eventType = participantType == ParticipantType.Teacher
                    ? ClassSessionEventType.TeacherLeftClass
                    : ClassSessionEventType.StudentLeft;
                isExpected = true;
                detail = null;
            }
            else
            {
                eventType = participantType == ParticipantType.Teacher
                    ? ClassSessionEventType.TeacherDisconnected
                    : ClassSessionEventType.StudentDisconnected;
                isExpected = classAlreadyOver;
                detail = classAlreadyOver
                    ? null
                    : $"Connection dropped without an explicit leave — session status was '{sessionInfo.Status}'.";
            }

            await WriteAsync(new ClassSessionEventLog
            {
                ClassSessionId = classSessionId,
                EventType = eventType,
                ParticipantType = participantType,
                TeacherProfileId = teacherProfileId,
                ChildId = childId,
                UserId = userId,
                ParticipantName = participantName,
                OccurredAtUtc = now,
                IsExpected = isExpected,
                Detail = detail,
            }, cancellationToken);
        }

        public async Task LogClassEndedAsync(ClassSession session, CancellationToken cancellationToken = default)
        {
            var teacherName = await _unitOfWork.Repository<TeacherProfile>().Query()
                .Where(t => t.Id == session.TeacherProfileId)
                .Select(t => t.User.FirstName + " " + t.User.LastName)
                .FirstOrDefaultAsync(cancellationToken);

            await WriteAsync(new ClassSessionEventLog
            {
                ClassSessionId = session.Id,
                EventType = ClassSessionEventType.ClassEnded,
                ParticipantType = ParticipantType.Teacher,
                TeacherProfileId = session.TeacherProfileId,
                ParticipantName = teacherName,
                OccurredAtUtc = DateTime.UtcNow,
                IsExpected = true,
            }, cancellationToken);
        }

        public async Task LogNoShowAsync(ClassSession session, NoShowParty party, CancellationToken cancellationToken = default)
        {
            if (party == NoShowParty.Teacher)
            {
                var teacherName = await _unitOfWork.Repository<TeacherProfile>().Query()
                    .Where(t => t.Id == session.TeacherProfileId)
                    .Select(t => t.User.FirstName + " " + t.User.LastName)
                    .FirstOrDefaultAsync(cancellationToken);

                await WriteAsync(new ClassSessionEventLog
                {
                    ClassSessionId = session.Id,
                    EventType = ClassSessionEventType.TeacherNoShow,
                    ParticipantType = ParticipantType.Teacher,
                    TeacherProfileId = session.TeacherProfileId,
                    ParticipantName = teacherName,
                    OccurredAtUtc = DateTime.UtcNow,
                    IsExpected = false,
                }, cancellationToken);
            }
            else
            {
                await WriteAsync(new ClassSessionEventLog
                {
                    ClassSessionId = session.Id,
                    EventType = ClassSessionEventType.StudentNoShow,
                    ParticipantType = ParticipantType.Student,
                    OccurredAtUtc = DateTime.UtcNow,
                    IsExpected = false,
                }, cancellationToken);
            }
        }

        public async Task LogJoinDeniedAsync(
            Guid classSessionId, Guid? userId, string reason, CancellationToken cancellationToken = default)
        {
            string? name = null;
            if (userId.HasValue)
            {
                name = await _unitOfWork.Repository<User>().Query()
                    .Where(u => u.Id == userId.Value)
                    .Select(u => u.FirstName + " " + u.LastName)
                    .FirstOrDefaultAsync(cancellationToken);
            }

            await WriteAsync(new ClassSessionEventLog
            {
                ClassSessionId = classSessionId,
                EventType = ClassSessionEventType.JoinDenied,
                UserId = userId,
                ParticipantName = name,
                OccurredAtUtc = DateTime.UtcNow,
                IsExpected = false,
                Detail = reason,
            }, cancellationToken);
        }

        /// <summary>
        /// Own isolated add-and-save so a logging failure (or a caller's own subsequent
        /// mutations being mid-flight) can never interfere with — or be interfered with by —
        /// the real action that triggered it. Never throws.
        /// </summary>
        private async Task WriteAsync(ClassSessionEventLog entry, CancellationToken cancellationToken)
        {
            try
            {
                await _unitOfWork.Repository<ClassSessionEventLog>().AddAsync(entry, cancellationToken);
                await _unitOfWork.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                // Best-effort: nothing that fires this write may ever fail because of it.
            }
        }
    }
}
