using iucs.readernest.api.Auth;
using iucs.readernest.api.Services;
using iucs.readernest.domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace iucs.readernest.api.Controllers
{
    /// <summary>
    /// Admin → Settings → File storage: where uploaded resources and presentations are kept right now,
    /// and the Sync buttons that copy every file between S3 and the server's own disk (StorageMigration)
    /// before switching Storage:Provider.
    /// </summary>
    [ApiController]
    [Route("api/storage")]
    public class StorageController : ControllerBase
    {
        private readonly StorageSyncService _sync;
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;
        private readonly IHttpContextAccessor _httpContext;

        public StorageController(StorageSyncService sync, IConfiguration configuration, IWebHostEnvironment environment, IHttpContextAccessor httpContext)
        {
            _sync = sync;
            _configuration = configuration;
            _environment = environment;
            _httpContext = httpContext;
        }

        public class StorageStatusDto
        {
            /// <summary>"S3" or "Local" -- where new uploads go and files are read from.</summary>
            public string Provider { get; set; } = "S3";
            public bool S3Configured { get; set; }
            public string LocalPath { get; set; } = "";
            public int LocalFileCount { get; set; }
            public long LocalBytes { get; set; }
            /// <summary>Free space on the local storage folder's disk (null if unknown).</summary>
            public long? LocalFreeBytes { get; set; }
            /// <summary>
            /// Whether the local folder is mounted from the host and so survives redeploys. False means
            /// files kept there would be lost on the next deploy (how files were lost before S3).
            /// Null when it can't be told (not Linux).
            /// </summary>
            public bool? LocalFolderMounted { get; set; }
            public StorageSyncProgress? Sync { get; set; }
        }

        public class StartSyncRequest
        {
            /// <summary>"local" (S3 → server disk) or "s3" (server disk → S3).</summary>
            public string To { get; set; } = "";
        }

        [HttpGet("status")]
        [HasPermission(PermissionModule.Settings, PermissionAction.View)]
        public ActionResult<StorageStatusDto> Status()
        {
            var local = new LocalFileStorage(_environment, _configuration, _httpContext);
            var files = local.ListKeys().Select(k => local.ExistingFilePath(k)).Where(p => p is not null).ToList();
            return Ok(new StorageStatusDto
            {
                Provider = string.Equals(_configuration["Storage:Provider"], "Local", StringComparison.OrdinalIgnoreCase) ? "Local" : "S3",
                S3Configured = new[] { "Endpoint", "AccessKey", "SecretKey", "BucketName" }
                    .All(k => !string.IsNullOrWhiteSpace(_configuration[$"Storage:S3:{k}"])),
                LocalPath = local.RootPath,
                LocalFileCount = files.Count,
                LocalBytes = files.Sum(p => System.IO.File.Exists(p) ? new FileInfo(p!).Length : 0),
                LocalFreeBytes = local.FreeBytes(),
                LocalFolderMounted = local.IsOnMountedVolume(),
                Sync = _sync.Current,
            });
        }

        [HttpPost("sync")]
        [HasPermission(PermissionModule.Settings, PermissionAction.Edit)]
        public ActionResult<StorageSyncProgress> Sync(StartSyncRequest request)
        {
            var to = request.To?.Trim().ToLowerInvariant();
            if (to is not ("local" or "s3"))
            {
                return BadRequest("Choose where to copy the files to: \"local\" or \"s3\".");
            }

            if (!_sync.TryStart(to))
            {
                return Conflict("A sync is already running -- wait for it to finish.");
            }

            return Ok(_sync.Current);
        }
    }
}
