using iucs.readernest.application.Dto.Admission;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Services
{
    public interface IDemoBookingService
    {
        Task<IReadOnlyList<DemoBookingDto>> ListAsync(ConversionStatus? status, CancellationToken cancellationToken = default);

        Task<DemoBookingDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

        Task<DemoBookingDto> CreateAsync(CreateDemoBookingRequest request, CancellationToken cancellationToken = default);

        /// <summary>Per-parent demo record: every demo each parent has taken, grouped by email, with fee totals.</summary>
        Task<IReadOnlyList<ParentDemoHistoryDto>> ListParentHistoryAsync(string? search, CancellationToken cancellationToken = default);

        Task<DemoBookingDto> UpdateConversionStatusAsync(
            Guid id,
            UpdateConversionStatusRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Teacher submits the mandatory post-demo feedback; the booking moves to
        /// DemoCompleted so the admission team can start follow-up.
        /// </summary>
        Task<DemoFeedbackDto> SubmitFeedbackAsync(
            Guid demoBookingId,
            Guid teacherUserId,
            SubmitDemoFeedbackRequest request,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<DemoFeedbackDto>> ListFeedbackAsync(CancellationToken cancellationToken = default);

        /// <summary>Demo bookings assigned to the signed-in teacher's sessions.</summary>
        Task<IReadOnlyList<DemoBookingDto>> ListForTeacherUserAsync(Guid userId, CancellationToken cancellationToken = default);

        /// <summary>The signed-in teacher's own submitted feedback.</summary>
        Task<IReadOnlyList<DemoFeedbackDto>> ListFeedbackForTeacherUserAsync(Guid userId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Manually override the teacher assigned to a demo booking (e.g. the auto-assigned or
        /// originally-picked teacher called in sick). Runs the same busy-slot check as booking
        /// creation, notifies both the newly-assigned and displaced teacher, and records the
        /// change in the audit trail (see <see cref="GetReassignmentHistoryAsync"/>).
        /// </summary>
        Task<DemoBookingDto> ReassignTeacherAsync(
            Guid bookingId,
            ReassignTeacherRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Every active teacher's load around the booking's slot, so staff can see who's
        /// free/light before overriding the assignment — not just a blind name dropdown.
        /// </summary>
        Task<IReadOnlyList<TeacherWorkloadDto>> GetTeacherWorkloadAsync(Guid bookingId, CancellationToken cancellationToken = default);

        /// <summary>Every manual teacher reassignment ever made on this booking, newest first.</summary>
        Task<IReadOnlyList<DemoReassignmentHistoryDto>> GetReassignmentHistoryAsync(Guid bookingId, CancellationToken cancellationToken = default);

        /// <summary>Every follow-up note ever logged on this booking, newest first, with the
        /// actual logging user and timestamp attached — see <see cref="UpdateConversionStatusAsync"/>.</summary>
        Task<IReadOnlyList<DemoBookingFollowUpDto>> GetFollowUpNotesAsync(Guid bookingId, CancellationToken cancellationToken = default);
    }
}
