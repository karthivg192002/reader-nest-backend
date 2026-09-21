using iucs.readernest.application.Dto.Resources;

namespace iucs.readernest.application.Services
{
    /// <summary>
    /// Folders in Content and Resources, and sharing a whole folder with any number of parents.
    /// Files are uploaded once into a folder; sharing the folder replaces re-uploading per parent.
    /// </summary>
    public interface IResourceFolderService
    {
        Task<IReadOnlyList<ResourceFolderDto>> ListAsync(CancellationToken cancellationToken = default);

        Task<ResourceFolderDto> CreateAsync(CreateResourceFolderRequest request, CancellationToken cancellationToken = default);

        /// <summary>Renames and/or moves a folder (its files and subfolders come along).</summary>
        Task<ResourceFolderDto> UpdateAsync(Guid id, UpdateResourceFolderRequest request, CancellationToken cancellationToken = default);

        /// <summary>Only an empty folder (no files, no subfolders) can be deleted.</summary>
        Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

        Task<IReadOnlyList<ResourceFolderAccessDto>> ListAccessAsync(Guid folderId, CancellationToken cancellationToken = default);

        /// <summary>Shares with / stops sharing with the given parents; unlimited, idempotent.</summary>
        Task<IReadOnlyList<ResourceFolderAccessDto>> SetAccessAsync(
            Guid folderId, Guid actorUserId, SetResourceFolderAccessRequest request, CancellationToken cancellationToken = default);

        /// <summary>Moves one file into a folder (null = top level).</summary>
        Task<ResourceDto> MoveResourceAsync(Guid resourceId, MoveResourceRequest request, CancellationToken cancellationToken = default);
    }
}
