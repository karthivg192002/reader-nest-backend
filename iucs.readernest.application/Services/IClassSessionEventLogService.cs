using iucs.readernest.application.Dto.Sessions;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Services
{
    /// <summary>
    /// Write side of the Class Session Logs feature — every method is best-effort (swallows
    /// its own failures, same contract as AcademicOpsService's CaptureJoin/LeaveAttendanceAsync)
    /// so a logging hiccup can never block the real join/leave/end/no-show it's recording.
    /// </summary>
    public interface IClassSessionEventLogService
    {
        /// <summary>Teacher joins the live room — first-ever join for this session logs
        /// TeacherStarted (and is the moment `ActualStartAtUtc`/Status are set by the caller);
        /// a later reconnect logs TeacherJoined instead.</summary>
        Task LogTeacherJoinAsync(
            ClassSession session, Guid teacherProfileId, Guid? userId, string participantName,
            bool isReconnect, CancellationToken cancellationToken = default);

        /// <summary>Student (via a parent account) joins the live room.</summary>
        Task LogStudentJoinAsync(
            ClassSession session, Guid childId, Guid? userId, string participantName,
            bool isReconnect, CancellationToken cancellationToken = default);

        /// <summary>A demo lead (no account) joins, matched by email against the booking.</summary>
        Task LogDemoParticipantJoinAsync(
            Guid classSessionId, string participantName, CancellationToken cancellationToken = default);

        /// <summary>Either party leaves the live room — <paramref name="wasExplicit"/>
        /// distinguishes a deliberate Leave/End from an abrupt connection drop. Takes a bare
        /// session id (rather than a loaded <see cref="ClassSession"/>) so the SignalR hub,
        /// which never loads the entity for its own purposes, doesn't need to.</summary>
        Task LogLeaveAsync(
            Guid classSessionId, ParticipantType participantType, Guid? teacherProfileId, Guid? childId,
            Guid? userId, string participantName, bool wasExplicit, CancellationToken cancellationToken = default);

        /// <summary>The definitive "End Class" action.</summary>
        Task LogClassEndedAsync(ClassSession session, CancellationToken cancellationToken = default);

        /// <summary>Teacher or student never joined within the no-show grace period.</summary>
        Task LogNoShowAsync(ClassSession session, NoShowParty party, CancellationToken cancellationToken = default);

        /// <summary>A join attempt was rejected — the caller isn't a genuine participant.</summary>
        Task LogJoinDeniedAsync(
            Guid classSessionId, Guid? userId, string reason, CancellationToken cancellationToken = default);
    }
}
