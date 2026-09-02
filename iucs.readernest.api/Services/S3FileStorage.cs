using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Common.Interfaces;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// S3-compatible object storage (Hetzner Object Storage, or any other S3-API provider —
    /// selected purely by ServiceURL). Chosen over the local-disk LocalFileStorage so uploaded
    /// resources survive a redeploy without depending on a correctly-mapped Docker volume,
    /// which local disk storage requires and which is easy to misconfigure.
    /// </summary>
    public class S3FileStorage : IFileStorage
    {
        // Same allowlist as LocalFileStorage — learning-resource types only, deliberately
        // excluding executables and script/markup types that could be served back with a
        // client-supplied Content-Type.
        private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf", ".doc", ".docx", ".ppt", ".pptx", ".xls", ".xlsx", ".txt",
            ".jpg", ".jpeg", ".png", ".gif", ".webp",
            ".mp4", ".webm", ".mov", ".mp3", ".wav", ".zip",
        };

        private readonly IAmazonS3 _client;
        private readonly string _bucket;

        public S3FileStorage(IConfiguration configuration)
        {
            // appsettings.json seeds these as "" (so the key names are discoverable), not
            // absent — a plain `?? throw` only catches a missing key, not that empty default,
            // and let this constructor build ServiceURL = "https://" from an unset endpoint
            // instead of failing with an actionable message.
            var endpoint = RequireConfigValue(configuration, "Storage:S3:Endpoint");
            var accessKey = RequireConfigValue(configuration, "Storage:S3:AccessKey");
            var secretKey = RequireConfigValue(configuration, "Storage:S3:SecretKey");
            _bucket = RequireConfigValue(configuration, "Storage:S3:BucketName");

            var config = new AmazonS3Config
            {
                ServiceURL = endpoint.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? endpoint : $"https://{endpoint}",
                // Hetzner (and most non-AWS S3-compatible providers) serve buckets as
                // bucket.endpoint rather than AWS's endpoint/bucket path style.
                ForcePathStyle = false,
            };
            _client = new AmazonS3Client(new BasicAWSCredentials(accessKey, secretKey), config);
        }

        private static string RequireConfigValue(IConfiguration configuration, string key)
        {
            var value = configuration[key];
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException(
                    $"{key} is not configured. Set the corresponding environment variable " +
                    $"({key.Replace(":", "__")}) — every resource-storage endpoint fails until it is.");
            }

            return value;
        }

        public async Task<StoredFile> StoreAsync(
            Stream content,
            string originalFileName,
            CancellationToken cancellationToken = default)
        {
            var extension = Path.GetExtension(originalFileName);
            if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
            {
                throw new DomainValidationException(
                    $"File type '{extension}' is not allowed. Supported types: " +
                    string.Join(", ", AllowedExtensions.OrderBy(e => e)) + ".");
            }

            var key = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";

            // S3's PutObject needs the content length up front and a seekable stream for
            // retries; the incoming upload stream (an ASP.NET Core IFormFile stream) is
            // neither guaranteed to be, so it's buffered to a MemoryStream first. Upload
            // sizes are already capped well below memory-pressure territory by
            // ResourcesController's RequestSizeLimit.
            await using var buffered = new MemoryStream();
            await content.CopyToAsync(buffered, cancellationToken);
            buffered.Position = 0;

            try
            {
                await _client.PutObjectAsync(
                    new PutObjectRequest
                    {
                        BucketName = _bucket,
                        Key = key,
                        InputStream = buffered,
                        AutoCloseStream = false,
                    },
                    cancellationToken);
            }
            catch (AmazonServiceException ex)
            {
                // Wraps auth/bucket/network failures from the S3-compatible endpoint --
                // otherwise these surface as an opaque 500 with no indication that the
                // problem is storage configuration (credentials, bucket, endpoint) rather
                // than the upload itself. See ExternalServiceException's summary.
                throw new ExternalServiceException(
                    $"Could not upload to object storage ({ex.StatusCode}): {ex.Message}. " +
                    "Check the Storage:S3 configuration (endpoint, credentials, bucket).");
            }

            return new StoredFile { RelativePath = key, SizeBytes = buffered.Length };
        }

        public async Task<Stream?> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _client.GetObjectAsync(_bucket, relativePath, cancellationToken);
                return response.ResponseStream;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
            catch (AmazonServiceException ex)
            {
                throw new ExternalServiceException(
                    $"Could not read from object storage ({ex.StatusCode}): {ex.Message}. " +
                    "Check the Storage:S3 configuration (endpoint, credentials, bucket).");
            }
        }
    }
}
