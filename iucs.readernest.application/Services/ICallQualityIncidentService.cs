using iucs.readernest.application.Dto.Monitoring;

namespace iucs.readernest.application.Services
{
    public interface ICallQualityIncidentService
    {
        /// <summary>Empty (never null) if main could not be reached -- degrades silently, same as the other main-SSH panels.</summary>
        Task<List<CallQualityIncidentDto>> GetRecentAsync(CancellationToken cancellationToken = default);
    }
}
