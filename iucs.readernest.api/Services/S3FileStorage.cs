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
    public class S3FileStorage : IFileStorage, IDirectUploadStorage
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

        // ---- IDirectUploadStorage: browser-to-bucket multipart upload and presigned playback ----

        /// <summary>64 MB parts: a multi-GB recording is a few dozen requests, and S3's 10,000-part cap
        /// still allows 640 GB. (S3's own minimum is 5 MB for every part but the last.)</summary>
        private const long PartSizeBytes = 64L * 1024 * 1024;

        public async Task<DirectUploadSession> StartMultipartAsync(
            string originalFileName, string? contentType, CancellationToken cancellationToken = default)
        {
            var extension = Path.GetExtension(originalFileName);
            if (string.IsNullOrEmpty(extension) || !AllowedExtensions.Contains(extension))
            {
                throw new DomainValidationException(
                    $"File type '{extension}' is not allowed. Supported types: " +
                    string.Join(", ", AllowedExtensions.OrderBy(e => e)) + ".");
            }

            var key = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
            try
            {
                var response = await _client.InitiateMultipartUploadAsync(
                    new InitiateMultipartUploadRequest
                    {
                        BucketName = _bucket,
                        Key = key,
                        ContentType = string.IsNullOrWhiteSpace(contentType) ? null : contentType,
                    },
                    cancellationToken);
                return new DirectUploadSession { Key = key, UploadId = response.UploadId, PartSizeBytes = PartSizeBytes };
            }
            catch (AmazonServiceException ex)
            {
                throw StorageFailure("start the upload", ex);
            }
        }

        public IReadOnlyList<PresignedPart> GetPartUrls(string key, string uploadId, IEnumerable<int> partNumbers)
        {
            return partNumbers.Distinct().Select(n => new PresignedPart
            {
                PartNumber = n,
                Url = _client.GetPreSignedURL(new GetPreSignedUrlRequest
                {
                    BucketName = _bucket,
                    Key = key,
                    Verb = HttpVerb.PUT,
                    UploadId = uploadId,
                    PartNumber = n,
                    // Long enough for a slow connection to push one 64 MB part.
                    Expires = DateTime.UtcNow.AddHours(2),
                }),
            }).ToList();
        }

        public async Task<long> CompleteMultipartAsync(
            string key, string uploadId, IReadOnlyList<UploadedPart> parts, CancellationToken cancellationToken = default)
        {
            try
            {
                await _client.CompleteMultipartUploadAsync(
                    new CompleteMultipartUploadRequest
                    {
                        BucketName = _bucket,
                        Key = key,
                        UploadId = uploadId,
                        PartETags = parts.OrderBy(p => p.PartNumber).Select(p => new PartETag(p.PartNumber, p.ETag)).ToList(),
                    },
                    cancellationToken);
                var head = await _client.GetObjectMetadataAsync(_bucket, key, cancellationToken);
                return head.ContentLength;
            }
            catch (AmazonServiceException ex)
            {
                throw StorageFailure("finish the upload", ex);
            }
        }

        public async Task AbortMultipartAsync(string key, string uploadId, CancellationToken cancellationToken = default)
        {
            try
            {
                await _client.AbortMultipartUploadAsync(
                    new AbortMultipartUploadRequest { BucketName = _bucket, Key = key, UploadId = uploadId },
                    cancellationToken);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // already gone -- nothing to clean up
            }
            catch (AmazonServiceException ex)
            {
                throw StorageFailure("cancel the upload", ex);
            }
        }

        /// <summary>
        /// Large uploads (over ~90 MB) go from the browser straight to the bucket with presigned
        /// PUTs, which the browser only allows if the BUCKET itself answers CORS for the portal's
        /// origin. Without it every part fails at the network level and the UI just says "The
        /// upload connection failed." Applied at startup so a fresh bucket works out of the box;
        /// idempotent, and best-effort (a key without the permission only logs a warning).
        /// </summary>
        public async Task EnsureBrowserUploadCorsAsync(IReadOnlyCollection<string> allowedOrigins, ILogger logger)
        {
            if (allowedOrigins.Count == 0)
            {
                return;
            }

            try
            {
                // PutBucketCors replaces the whole configuration, so keep whatever rules the bucket
                // already has and only add ours (skipped when an equivalent rule is already there).
                var rules = new List<CORSRule>();
                try
                {
                    rules.AddRange((await _client.GetCORSConfigurationAsync(_bucket)).Configuration.Rules);
                }
                catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    // no CORS configuration yet
                }

                var alreadyCovered = rules.Any(r =>
                    r.AllowedMethods.Contains("PUT")
                    && r.ExposeHeaders.Any(h => h.Equals("ETag", StringComparison.OrdinalIgnoreCase))
                    && (r.AllowedOrigins.Contains("*") || allowedOrigins.All(o => r.AllowedOrigins.Contains(o))));
                if (alreadyCovered)
                {
                    return;
                }

                rules.Add(new CORSRule
                {
                    AllowedOrigins = allowedOrigins.ToList(),
                    AllowedMethods = ["GET", "PUT", "HEAD"],
                    AllowedHeaders = ["*"],
                    // The browser must read each part's ETag back to complete the upload.
                    ExposeHeaders = ["ETag"],
                    MaxAgeSeconds = 3000,
                });
                await _client.PutCORSConfigurationAsync(new PutCORSConfigurationRequest
                {
                    BucketName = _bucket,
                    Configuration = new CORSConfiguration { Rules = rules },
                });
                logger.LogInformation("Bucket CORS applied for browser uploads ({Origins}).", string.Join(", ", allowedOrigins));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Could not set the bucket's CORS rules — uploads over 90 MB will fail until the bucket allows PUT/GET from {Origins} and exposes ETag.",
                    string.Join(", ", allowedOrigins));
            }
        }

        public string GetReadUrl(string key, TimeSpan validFor, string? contentType)
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = _bucket,
                Key = key,
                Verb = HttpVerb.GET,
                Expires = DateTime.UtcNow.Add(validFor),
            };
            // "inline" so the browser plays it rather than saving it.
            request.ResponseHeaderOverrides.ContentDisposition = "inline";
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                request.ResponseHeaderOverrides.ContentType = contentType;
            }

            return _client.GetPreSignedURL(request);
        }

        private static ExternalServiceException StorageFailure(string action, AmazonServiceException ex) =>
            new($"Could not {action} in object storage ({ex.StatusCode}): {ex.Message}. " +
                "Check the Storage:S3 configuration (endpoint, credentials, bucket).");

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
