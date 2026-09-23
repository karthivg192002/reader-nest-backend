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

        /// <summary>Files an existing class recording into Content and Resources BY REFERENCE (the
        /// Resource points at the recording's own stored file, nothing is copied), so it can be
        /// shared with any number of parents/batches through the normal folder sharing — the
        /// "upload a sold recording once" flow. View-only, never downloadable.</summary>
        Task<ResourceDto> CreateFromRecordingAsync(AddRecordingResourceRequest request, CancellationToken cancellationToken = default);

        /// <summary>Returns the entity (with its storage path) for download streaming.</summary>
        Task<Resource> GetForDownloadAsync(Guid id, CancellationToken cancellationToken = default);

        /// <summary>Download streaming scoped to a resource the teacher owns (403 otherwise).</summary>
        Task<Resource> GetForTeacherDownloadAsync(Guid userId, Guid id, CancellationToken cancellationToken = default);

        Task GrantAccessAsync(Guid resourceId, GrantResourceAccessRequest request, CancellationToken cancellationToken = default);

        /// <summary>Removes a file from Content and Resources (and every parent/batch grant on it).
        /// Only the entry is removed — the stored object is left in place, since a recording filed
        /// by reference shares its file with the original class recording.</summary>
        Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

        Task<ResourceDto> UpdateAsync(Guid id, UpdateResourceRequest request, CancellationToken cancellationToken = default);
    }
}
