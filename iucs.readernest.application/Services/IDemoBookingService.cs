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
    }
}
