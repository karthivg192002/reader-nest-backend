using iucs.readernest.domain.Entities.Common;
using iucs.readernest.domain.Entities.Users;
using iucs.readernest.domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Sessions
{
    /// <summary>
    /// Login/join-based attendance capture per session participant.
    /// Exactly one of ChildId (student rows) or TeacherProfileId (teacher rows) is set,
    /// discriminated by ParticipantType.
    /// </summary>
    [Index(nameof(ClassSessionId))]
    public class SessionAttendance : BaseEntity
    {
        public Guid ClassSessionId { get; set; }

        public ClassSession ClassSession { get; set; } = null!;

        public ParticipantType ParticipantType { get; set; }

        public Guid? ChildId { get; set; }

        public Child? Child { get; set; }

        public Guid? TeacherProfileId { get; set; }

        public TeacherProfile? TeacherProfile { get; set; }

        public DateTime? JoinedAtUtc { get; set; }

        public DateTime? LeftAtUtc { get; set; }

        public AttendanceStatus Status { get; set; }
    }
}
