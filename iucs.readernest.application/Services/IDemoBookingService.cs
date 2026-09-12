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
        /// Manually re-sends the demo's join link to the parent, every extra invitee, and the
        /// assigned teacher — for when a parent reports never getting (or losing) the original
        /// confirmation email. Always uses the teacher's current fixed personal room.
        /// </summary>
        Task<DemoBookingDto> ResendLinkAsync(Guid bookingId, CancellationToken cancellationToken = default);

        /// <summary>The parent's join link for this demo, for staff to copy and share manually. Never expires.</summary>
        Task<string> GetJoinLinkAsync(Guid bookingId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Resolves a parent/participant's demo join link fresh, right now — the current Jitsi
        /// domain and a newly-signed token, never whatever was baked into an email or copied
        /// link at some earlier moment. Backs the public GET /api/demo-bookings/{id}/join
        /// redirect that the confirmation email, resend and "Copy Link" now all point at instead
        /// of a static URL, so a Jitsi domain change (or just time passing) can't leave a parent
        /// holding a dead link the way a frozen one could — reported live as a parent's join
        /// link 404-ing while teachers, who always re-resolve fresh through the authenticated
        /// app, kept joining fine.
        /// <paramref name="participantId"/> null means the primary parent; otherwise the id of
        /// one of the booking's extra invitees.
        /// Deliberately no time-based expiry — see the implementation's own remarks. Returns null
        /// only when there's nothing to resolve at all: no such booking/session, no room yet, or
        /// the given participant id doesn't belong to this booking. The link dies only when the
        /// booking itself is deleted (<see cref="DeleteAsync"/>), never on a clock.
        /// </summary>
        Task<string?> ResolveLiveJoinUrlAsync(Guid bookingId, Guid? participantId, CancellationToken cancellationToken = default);

        /// <summary>
        /// Permanently removes a demo booking staff no longer want on the books (a test entry, a
        /// mistaken double-booking) — the booking, its extra invitees and any submitted feedback,
        /// and cancels the linked class session so the teacher's slot frees up and the join link
        /// stops resolving. Refuses once the lead has already converted to real money — invoiced
        /// or Enrolled — since that has its own history to preserve; change its conversion status
        /// instead of deleting those.
        /// </summary>
        Task DeleteAsync(Guid bookingId, CancellationToken cancellationToken = default);

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
