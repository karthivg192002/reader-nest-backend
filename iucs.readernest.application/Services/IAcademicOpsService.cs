using iucs.readernest.application.Dto.Academics;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Services
{
    public interface IAcademicOpsService
    {
        // Holiday calendar management
        Task<IReadOnlyList<HolidayDto>> ListHolidaysAsync(CancellationToken cancellationToken = default);

        Task<HolidayDto> CreateHolidayAsync(SaveHolidayRequest request, CancellationToken cancellationToken = default);

        Task DeleteHolidayAsync(Guid id, CancellationToken cancellationToken = default);

        // Teacher leave workflow
        /// <summary>
        /// 6-hour restriction: a leave that starts within 6 hours and covers a scheduled
        /// session is auto-blocked. Admins are notified of every submission.
        /// </summary>
        Task<LeaveRequestDto> SubmitLeaveAsync(Guid teacherUserId, SubmitLeaveRequest request, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<LeaveRequestDto>> ListLeaveAsync(LeaveStatus? status, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<LeaveRequestDto>> ListLeaveForTeacherUserAsync(Guid teacherUserId, CancellationToken cancellationToken = default);

        Task<LeaveRequestDto> ReviewLeaveAsync(Guid id, ReviewLeaveRequest request, CancellationToken cancellationToken = default);

        // Attendance capture
        Task<IReadOnlyList<SessionAttendanceDto>> CaptureAttendanceAsync(
            Guid sessionId,
            CaptureAttendanceRequest request,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<SessionAttendanceDto>> ListAttendanceAsync(Guid sessionId, CancellationToken cancellationToken = default);
    }
}
