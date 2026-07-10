namespace iucs.readernest.application.Common.Interfaces
{
    public class StoredFile
    {
        public string RelativePath { get; set; } = null!;

        public long SizeBytes { get; set; }
    }

    /// <summary>
    /// Blob storage for uploaded learning content. Sprint 1 ships a local-disk
    /// implementation in the API layer; swaps for client-owned cloud storage later.
    /// </summary>
    public interface IFileStorage
    {
        Task<StoredFile> StoreAsync(Stream content, string originalFileName, CancellationToken cancellationToken = default);

        /// <summary>Resolves a stored relative path to an absolute path for streaming.</summary>
        string GetAbsolutePath(string relativePath);
    }
}
