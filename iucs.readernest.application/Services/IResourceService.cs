using iucs.readernest.application.Dto.Resources;
using iucs.readernest.domain.Entities.Resources;
using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Services
{
    public interface IResourceService
    {
        Task<IReadOnlyList<ResourceDto>> ListAsync(ResourceType? type, CancellationToken cancellationToken = default);

        /// <summary>Resources tied to the batches (or their courses) the signed-in teacher owns.</summary>
        Task<IReadOnlyList<ResourceDto>> ListForTeacherUserAsync(Guid userId, ResourceType? type, CancellationToken cancellationToken = default);

        Task<ResourceDto> CreateAsync(
            CreateResourceRequest request,
            string storedRelativePath,
            string? mimeType,
            long sizeBytes,
            CancellationToken cancellationToken = default);

        /// <summary>Teacher upload, validated to one of the teacher's own batches.</summary>
        Task<ResourceDto> CreateForTeacherUserAsync(
            Guid userId,
            CreateResourceRequest request,
            string storedRelativePath,
            string? mimeType,
            long sizeBytes,
            CancellationToken cancellationToken = default);

        /// <summary>Returns the entity (with its storage path) for download streaming.</summary>
        Task<Resource> GetForDownloadAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>Download streaming scoped to a resource the teacher owns (403 otherwise).</summary>
        Task<Resource> GetForTeacherDownloadAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);

        Task GrantAccessAsync(Guid resourceId, GrantResourceAccessRequest request, CancellationToken cancellationToken = default);
    }
}
