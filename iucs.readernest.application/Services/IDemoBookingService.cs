using iucs.readernest.application.Dto.Admission;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Services
{
    public interface IDemoBookingService
    {
        Task<IReadOnlyList<DemoBookingDto>> ListAsync(ConversionStatus? status, CancellationToken cancellationToken = default);

        Task<DemoBookingDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

        Task<DemoBookingDto> CreateAsync(CreateDemoBookingRequest request, CancellationToken cancellationToken = default);

        Task<DemoBookingDto> UpdateConversionStatusAsync(
            Guid id,
            UpdateConversionStatusRequest request,
            CancellationToken cancellationToken = default);
    }
}
