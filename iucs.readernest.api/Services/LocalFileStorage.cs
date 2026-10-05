using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Disk-backed storage for uploaded resources and class presentations, selected with
    /// Storage:Provider = "Local" (S3FileStorage is the default). Files land under Storage:LocalPath
    /// with the same flat "{guid}{ext}" keys S3FileStorage uses, so the database never changes when
    /// moving between the two (see StorageMigration).
    ///
    /// It also does everything IDirectUploadStorage needs, the way an object store would: large
    /// files are PUT by the browser in parts to short-lived signed URLs (served by
    /// LocalStorageController) and played back through a signed, seekable read URL. Signatures are
    /// HMACs over the request with Jwt:SigningKey, so a URL can't be forged or reused for another
    /// file and expires on its own.
    ///
    /// IMPORTANT for deploys: Storage:LocalPath must be a mounted host folder. Without one the files
    /// live in the container's writable layer and are lost on the next redeploy -- that's exactly why
    /// the app moved to S3 in the first place.
    /// </summary>
    public class LocalFileStorage : IFileStorage, IDirectUploadStorage
    {
        // Learning-resource types only: worksheets/books/slides, images, audio/video,
        // zipped bundles. Deliberately excludes executables and script/markup types
        // (.exe/.sh/.js/.html/.svg/...) that could be uploaded under a resource's file
        // slot and later served back with a client-supplied Content-Type.
        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".doc", ".docx", ".ppt", ".pptx", ".xls", ".xlsx", ".txt", ".epub",
            ".jpg", ".jpeg", ".png", ".gif", ".webp",
            ".mp4", ".webm", ".mov", ".mp3", ".wav", ".zip",
        };

        /// <summary>Same part size as S3FileStorage, so the browser's upload code behaves identically.</summary>
        public const long PartSizeBytes = 64L * 1024 * 1024;

        /// <summary>Unfinished multipart uploads live here, one folder per upload, until completed or aborted.</summary>
        private const string PartsFolder = ".multipart";

        private static readonly Regex KeyPattern = new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,200}$", RegexOptions.Compiled);
        private static readonly Regex UploadIdPattern = new(@"^[a-f0-9]{32}$", RegexOptions.Compiled);

        private readonly string _rootPath;
        private readonly byte[] _signingKey;
        private readonly string? _configuredBaseUrl;
        private readonly IHttpContextAccessor _httpContext;

        public LocalFileStorage(IWebHostEnvironment environment, IConfiguration configuration, IHttpContextAccessor httpContext)
        {
            _rootPath = Path.GetFullPath(configuration["Storage:LocalPath"] is { Length: > 0 } configured
                ? configured
                : Path.Combine(environment.ContentRootPath, "uploads"));
            _signingKey = Encoding.UTF8.GetBytes(configuration["Jwt:SigningKey"] is { Length: > 0 } key ? key : "local-storage-dev-key");
            _configuredBaseUrl = configuration["Api:BaseUrl"]?.TrimEnd('/');
            _httpContext = httpContext;
        }

        public string RootPath => _rootPath;

        public async Task<StoredFile> StoreAsync(
            Stream content,
            string originalFileName,
            CancellationToken cancellationToken = default)
        {
            var key = NewKey(originalFileName);
            var size = await SaveAsync(key, content, cancellationToken);
            return new StoredFile { RelativePath = key, SizeBytes = size };
        }

        /// <summary>Writes a file under an exact key (used by StoreAsync and the S3 -> local migration).</summary>
        public async Task<long> SaveAsync(string key, Stream content, CancellationToken cancellationToken = default)
        {
            var target = PathFor(key);
            Directory.CreateDirectory(_rootPath);
            // Written to a temp name and moved into place, so a half-written file is never served.
            var temp = target + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await using (var file = File.Create(temp))
                {
                    await content.CopyToAsync(file, cancellationToken);
                }

                File.Move(temp, target, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }

            return new FileInfo(target).Length;
        }

        public Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            var path = TryPathFor(relativePath);
            return Task.FromResult<Stream?>(path is not null && File.Exists(path) ? File.OpenRead(path) : null);
        }

        /// <summary>The file's absolute path when it exists, for range-capable serving.</summary>
        public string? ExistingFilePath(string key)
        {
            var path = TryPathFor(key);
            return path is not null && File.Exists(path) ? path : null;
        }

        /// <summary>Every stored file's key (for StorageMigration), skipping in-progress uploads.</summary>
        public IEnumerable<string> ListKeys()
        {
            if (!Directory.Exists(_rootPath))
            {
                yield break;
            }

            foreach (var path in Directory.EnumerateFiles(_rootPath))
            {
                var name = Path.GetFileName(path);
                if (KeyPattern.IsMatch(name) && !name.Contains(".tmp-", StringComparison.Ordinal))
                {
                    yield return name;
                }
            }
        }

        // ---- IDirectUploadStorage ----

        public Task<DirectUploadSession> StartMultipartAsync(string originalFileName, string? contentType, CancellationToken cancellationToken = default)
        {
            var key = NewKey(originalFileName);
            var uploadId = Guid.NewGuid().ToString("N");
            var folder = PartsFolderFor(uploadId);
            Directory.CreateDirectory(folder);
            // The key this upload belongs to, checked on every part and on completion.
            File.WriteAllText(Path.Combine(folder, "key"), key);
            return Task.FromResult(new DirectUploadSession { Key = key, UploadId = uploadId, PartSizeBytes = PartSizeBytes });
        }

        public IReadOnlyList<PresignedPart> GetPartUrls(string key, string uploadId, IEnumerable<int> partNumbers)
        {
            EnsureUploadBelongsTo(uploadId, key);
            var expires = DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds(); // long enough for one 64 MB part on a slow line
            return partNumbers.Distinct().Select(n => new PresignedPart
            {
                PartNumber = n,
                Url = $"{BaseUrl()}/api/storage/local/parts/{uploadId}/{n}?key={Uri.EscapeDataString(key)}&exp={expires}" +
                      $"&sig={Sign(PartPayload(uploadId, key, n, expires))}",
            }).ToList();
        }

        /// <summary>Saves one uploaded part (called by LocalStorageController once the URL's signature checks out); returns its ETag.</summary>
        public async Task<string> SavePartAsync(string uploadId, string key, int partNumber, Stream content, CancellationToken cancellationToken)
        {
            EnsureUploadBelongsTo(uploadId, key);
            if (partNumber is < 1 or > 10_000)
            {
                throw new DomainValidationException("Invalid part number.");
            }

            var target = Path.Combine(PartsFolderFor(uploadId), $"part-{partNumber:D5}");
            var temp = target + ".tmp";
            using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            long written = 0;
            await using (var file = File.Create(temp))
            {
                var buffer = new byte[81920];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    written += read;
                    if (written > PartSizeBytes)
                    {
                        throw new DomainValidationException("This part is larger than the agreed part size.");
                    }

                    md5.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            File.Move(temp, target, overwrite: true);
            return $"\"{Convert.ToHexString(md5.GetHashAndReset()).ToLowerInvariant()}\"";
        }

        public async Task<long> CompleteMultipartAsync(string key, string uploadId, IReadOnlyList<UploadedPart> parts, CancellationToken cancellationToken = default)
        {
            EnsureUploadBelongsTo(uploadId, key);
            var folder = PartsFolderFor(uploadId);
            var ordered = parts.Select(p => p.PartNumber).Distinct().OrderBy(n => n).ToList();
            if (ordered.Count == 0 || ordered[0] != 1 || ordered[^1] != ordered.Count)
            {
                throw new DomainValidationException("The upload is missing parts -- please try uploading the file again.");
            }

            var target = PathFor(key);
            var temp = target + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                await using (var output = File.Create(temp))
                {
                    foreach (var n in ordered)
                    {
                        var partPath = Path.Combine(folder, $"part-{n:D5}");
                        if (!File.Exists(partPath))
                        {
                            throw new DomainValidationException($"Part {n} of the upload never arrived -- please try uploading the file again.");
                        }

                        await using var part = File.OpenRead(partPath);
                        await part.CopyToAsync(output, cancellationToken);
                    }
                }

                File.Move(temp, target, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }

            Directory.Delete(folder, recursive: true);
            return new FileInfo(target).Length;
        }

        public Task AbortMultipartAsync(string key, string uploadId, CancellationToken cancellationToken = default)
        {
            if (UploadIdPattern.IsMatch(uploadId) && Directory.Exists(PartsFolderFor(uploadId)))
            {
                Directory.Delete(PartsFolderFor(uploadId), recursive: true);
            }

            return Task.CompletedTask;
        }

        public string GetReadUrl(string key, TimeSpan validFor, string? contentType)
        {
            var expires = DateTimeOffset.UtcNow.Add(validFor).ToUnixTimeSeconds();
            var type = contentType ?? string.Empty;
            return $"{BaseUrl()}/api/storage/local/files/{Uri.EscapeDataString(key)}?exp={expires}" +
                   (type.Length > 0 ? $"&ct={Uri.EscapeDataString(type)}" : string.Empty) +
                   $"&sig={Sign(ReadPayload(key, expires, type))}";
        }

        // ---- signatures (checked by LocalStorageController) ----

        public bool IsValidPartSignature(string uploadId, string key, int partNumber, long expires, string? signature) =>
            NotExpired(expires) && SignatureMatches(PartPayload(uploadId, key, partNumber, expires), signature);

        public bool IsValidReadSignature(string key, long expires, string? contentType, string? signature) =>
            NotExpired(expires) && SignatureMatches(ReadPayload(key, expires, contentType ?? string.Empty), signature);

        private static string PartPayload(string uploadId, string key, int partNumber, long expires) => $"put|{uploadId}|{key}|{partNumber}|{expires}";

        private static string ReadPayload(string key, long expires, string contentType) => $"get|{key}|{expires}|{contentType}";

        private static bool NotExpired(long expires) => DateTimeOffset.UtcNow.ToUnixTimeSeconds() <= expires;

        private string Sign(string payload) =>
            Convert.ToHexString(HMACSHA256.HashData(_signingKey, Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

        private bool SignatureMatches(string payload, string? signature) =>
            signature is not null
            && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Sign(payload)), Encoding.ASCII.GetBytes(signature.ToLowerInvariant()));

        // ---- helpers ----

        /// <summary>
        /// Links point back at this API. The current request's own host (behind the proxy, via the
        /// forwarded headers) is preferred so UAT and live each link to themselves; Api:BaseUrl is
        /// the fallback outside a request.
        /// </summary>
        private string BaseUrl()
        {
            var request = _httpContext.HttpContext?.Request;
            if (request is not null && request.Host.HasValue)
            {
                return $"{request.Scheme}://{request.Host}{request.PathBase}".TrimEnd('/');
            }

            return _configuredBaseUrl ?? string.Empty;
        }

        private static string NewKey(string originalFileName)
        {
            var extension = Path.GetExtension(originalFileName);
            if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
            {
                throw new DomainValidationException(
                    $"File type '{extension}' is not allowed. Supported types: " +
                    string.Join(", ", AllowedExtensions.OrderBy(e => e)) + ".");
            }

            return $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        }

        private string PathFor(string key) =>
            TryPathFor(key) ?? throw new DomainValidationException("Invalid file name.");

        /// <summary>Keys are flat file names; anything else (folders, "..") is refused so a key can never point outside the storage folder.</summary>
        private string? TryPathFor(string key)
        {
            if (string.IsNullOrWhiteSpace(key) || !KeyPattern.IsMatch(key) || key.Contains("..", StringComparison.Ordinal))
            {
                return null;
            }

            var path = Path.GetFullPath(Path.Combine(_rootPath, key));
            return path.StartsWith(_rootPath + Path.DirectorySeparatorChar, StringComparison.Ordinal) ? path : null;
        }

        private string PartsFolderFor(string uploadId) => Path.Combine(_rootPath, PartsFolder, uploadId);

        private void EnsureUploadBelongsTo(string uploadId, string key)
        {
            var marker = UploadIdPattern.IsMatch(uploadId) ? Path.Combine(PartsFolderFor(uploadId), "key") : null;
            if (marker is null || !File.Exists(marker) || File.ReadAllText(marker) != key)
            {
                throw new DomainValidationException("This upload has expired or doesn't exist -- please start the upload again.");
            }
        }
    }
}
