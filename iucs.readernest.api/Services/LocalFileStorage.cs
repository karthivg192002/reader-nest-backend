using iucs.readernest.application.Common.Interfaces;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Local-disk implementation for development: files land under
    /// {ContentRoot}/uploads with a GUID name (original name preserved in metadata).
    /// Replace with client-owned cloud storage for production.
    /// </summary>
    public class LocalFileStorage : IFileStorage
    {
        private readonly string _rootPath;

        public LocalFileStorage(IWebHostEnvironment environment, IConfiguration configuration)
        {
            _rootPath = configuration["Storage:LocalPath"]
                ?? Path.Combine(environment.ContentRootPath, "uploads");
        }

        public async Task<StoredFile> StoreAsync(
            Stream content,
            string originalFileName,
            CancellationToken cancellationToken = default)
        {
            Directory.CreateDirectory(_rootPath);

            var extension = Path.GetExtension(originalFileName);
            var relativePath = $"{Guid.NewGuid():N}{extension}";
            var absolutePath = Path.Combine(_rootPath, relativePath);

            await using var target = File.Create(absolutePath);
            await content.CopyToAsync(target, cancellationToken);

            return new StoredFile { RelativePath = relativePath, SizeBytes = target.Length };
        }

        public string GetAbsolutePath(string relativePath)
        {
            return Path.Combine(_rootPath, relativePath);
        }
    }
}
