using System.Collections.Concurrent;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// The IFileStorage / IDirectUploadStorage the app uses: sends new uploads to whichever store
    /// StorageProviderSwitch says is active, and finds existing files in either one. Keys are the
    /// same in both stores, so a file uploaded before a switch keeps working after it -- read from
    /// where it actually is until a Sync copies it across.
    /// </summary>
    public class RoutingFileStorage : IFileStorage, IDirectUploadStorage
    {
        private readonly StorageProviderSwitch _switch;
        private readonly LocalFileStorage _local;
        private readonly S3FileStorage? _s3;

        /// <summary>Keys confirmed to be in S3, so a file with a local copy isn't checked again on every view.</summary>
        private readonly ConcurrentDictionary<string, bool> _knownInS3 = new(StringComparer.Ordinal);

        /// <param name="local">Falls back to <paramref name="s3"/> itself for files not on this server yet.</param>
        public RoutingFileStorage(StorageProviderSwitch providerSwitch, LocalFileStorage local, S3FileStorage? s3)
        {
            _switch = providerSwitch;
            _local = local;
            _s3 = s3;
        }

        private bool UseS3 => _s3 is not null && !_switch.UseLocal;

        public Task<StoredFile> StoreAsync(Stream content, string originalFileName, CancellationToken cancellationToken = default) =>
            UseS3 ? _s3!.StoreAsync(content, originalFileName, cancellationToken) : _local.StoreAsync(content, originalFileName, cancellationToken);

        public async Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            if (!UseS3)
            {
                return await _local.OpenReadAsync(relativePath, cancellationToken);
            }

            // On S3, but the file may only be on this server (uploaded while it was set to Local).
            var localPath = _local.ExistingFilePath(relativePath);
            try
            {
                var stream = await _s3!.OpenReadAsync(relativePath, cancellationToken);
                if (stream is not null || localPath is null)
                {
                    return stream;
                }
            }
            catch (ExternalServiceException) when (localPath is not null)
            {
                // S3 unreachable, but there's a copy right here.
            }

            return File.OpenRead(localPath);
        }

        // ---- IDirectUploadStorage ----

        public Task<DirectUploadSession> StartMultipartAsync(string originalFileName, string? contentType, CancellationToken cancellationToken = default) =>
            UseS3 ? _s3!.StartMultipartAsync(originalFileName, contentType, cancellationToken) : _local.StartMultipartAsync(originalFileName, contentType, cancellationToken);

        // An upload already under way finishes in the store it started in, even if storage is
        // switched while it runs -- told apart by the upload id, which only local storage makes as 32 hex characters.
        public IReadOnlyList<PresignedPart> GetPartUrls(string key, string uploadId, IEnumerable<int> partNumbers) =>
            StoreFor(uploadId).GetPartUrls(key, uploadId, partNumbers);

        public Task<long> CompleteMultipartAsync(string key, string uploadId, IReadOnlyList<UploadedPart> parts, CancellationToken cancellationToken = default) =>
            StoreFor(uploadId).CompleteMultipartAsync(key, uploadId, parts, cancellationToken);

        public Task AbortMultipartAsync(string key, string uploadId, CancellationToken cancellationToken = default) =>
            StoreFor(uploadId).AbortMultipartAsync(key, uploadId, cancellationToken);

        public string GetReadUrl(string key, TimeSpan validFor, string? contentType)
        {
            if (!UseS3)
            {
                return _local.GetReadUrl(key, validFor, contentType); // falls back to S3 for files not copied yet
            }

            // On S3 with a copy here as well: link to S3 if it has the file, otherwise (uploaded
            // while on Local and not synced yet, or S3 unreachable) to the copy on this server.
            if (_local.ExistingFilePath(key) is not null && !_knownInS3.ContainsKey(key))
            {
                if (_s3!.ExistsAsync(key).GetAwaiter().GetResult() != true)
                {
                    return _local.GetReadUrl(key, validFor, contentType);
                }

                _knownInS3[key] = true;
            }

            return _s3!.GetReadUrl(key, validFor, contentType);
        }

        private IDirectUploadStorage StoreFor(string uploadId) =>
            _s3 is null || LocalFileStorage.IsLocalUploadId(uploadId) ? _local : _s3;
    }
}
