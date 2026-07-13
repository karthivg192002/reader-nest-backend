using iucs.readernest.application.Dto.Integrations;

namespace iucs.readernest.application.Services
{
    public interface IIntegrationService
    {
        Task<IReadOnlyList<IntegrationDto>> ListAsync(CancellationToken cancellationToken = default);

        Task<IntegrationDto> CreateAsync(SaveIntegrationRequest request, CancellationToken cancellationToken = default);

        Task<IntegrationDto> UpdateAsync(Guid id, SaveIntegrationRequest request, CancellationToken cancellationToken = default);

        Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    }
}
