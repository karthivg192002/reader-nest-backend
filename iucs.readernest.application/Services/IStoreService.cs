using iucs.readernest.application.Dto.Admission;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Services
{
    /// <summary>Backs the public course catalog (/store) and the admin follow-up queue for it.</summary>
    public interface IStoreService
    {
        /// <summary>Active plans only — this is the public-facing catalog.</summary>
        Task<IReadOnlyList<StorePlanDto>> ListPublicPlansAsync(CancellationToken cancellationToken = default);

        Task<StoreInquiryDto> CreateInquiryAsync(CreateStoreInquiryRequest request, CancellationToken cancellationToken = default);

        /// <summary>Public self-booking: always auto-assigns a teacher, fixed 30-minute slot, no login.</summary>
        Task<StoreDemoBookingConfirmationDto> BookDemoAsync(CreateStoreDemoBookingRequest request, CancellationToken cancellationToken = default);

        /// <summary>
        /// Every bookable 30-minute demo start time on <paramref name="date"/> (org business
        /// hours, 9am-7pm IST) that still has at least one matching active teacher free — lets
        /// the public booking form show real openings instead of a visitor guessing a time and
        /// hitting "no teacher available".
        /// </summary>
        Task<IReadOnlyList<AvailableDemoSlotDto>> ListAvailableDemoSlotsAsync(
            DateOnly date,
            Guid? departmentId,
            CancellationToken cancellationToken = default);

        Task<IReadOnlyList<StoreInquiryDto>> ListInquiriesAsync(StoreInquiryStatus? status, CancellationToken cancellationToken = default);

        Task<StoreInquiryDto> UpdateInquiryStatusAsync(
            Guid id,
            UpdateStoreInquiryStatusRequest request,
            CancellationToken cancellationToken = default);
    }
}
