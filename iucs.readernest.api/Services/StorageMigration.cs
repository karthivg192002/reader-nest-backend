namespace iucs.readernest.api.Services
{
    /// <summary>Live state of a storage sync, shown on Admin → Settings → File storage.</summary>
    public class StorageSyncProgress
    {
        /// <summary>"local" (S3 → server disk) or "s3" (server disk → S3).</summary>
        public string To { get; set; } = "";
        public bool Running { get; set; }
        public int Total { get; set; }
        public int Copied { get; set; }
        public int Skipped { get; set; }
        public int Failed { get; set; }
        public long CopiedBytes { get; set; }
        public DateTime? StartedAtUtc { get; set; }
        public DateTime? FinishedAtUtc { get; set; }
        /// <summary>A setup problem that stopped the sync before any file was tried (e.g. S3 not configured).</summary>
        public string? Error { get; set; }
        /// <summary>The first few files that failed, with why.</summary>
        public List<string> FailedFiles { get; set; } = [];
    }

    /// <summary>
    /// Copies every uploaded file between S3 and the local storage folder, keeping each file's key --
    /// the database stores only keys, so nothing in it needs to change. Started from the "Sync"
    /// buttons on Admin → Settings (StorageController), or from the command line inside the API
    /// container (it reads the same Storage:* settings the app uses; both sides must be set):
    ///
    ///   docker exec readernestbackend dotnet iucs.readernest.api.dll storage-migrate --to local
    ///   docker exec readernestbackend dotnet iucs.readernest.api.dll storage-migrate --to s3
    ///
    /// Safe to re-run: files already present with the same size are skipped, and nothing is ever
    /// deleted from the source. Switch Storage:Provider only after it reports no failures.
    /// </summary>
    public static class StorageMigration
    {
        private const int MaxFailedFilesListed = 20;

        public static bool IsRequested(string[] args) =>
            args.Length > 0 && string.Equals(args[0], "storage-migrate", StringComparison.OrdinalIgnoreCase);

        /// <summary>Command-line entry point; prints progress and returns the process exit code.</summary>
        public static async Task<int> RunAsync(string[] args, IServiceProvider services, IConfiguration configuration)
        {
            var to = args.SkipWhile(a => !string.Equals(a, "--to", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault()?.ToLowerInvariant();
            if (to is not ("local" or "s3"))
            {
                Console.WriteLine("Usage: storage-migrate --to local   (copy every S3 file into Storage:LocalPath)");
                Console.WriteLine("       storage-migrate --to s3      (copy every local file into the S3 bucket)");
                return 2;
            }

            var progress = new StorageSyncProgress();
            await SyncAsync(to, services, configuration, progress, CancellationToken.None);
            if (progress.Error is not null)
            {
                Console.WriteLine(progress.Error);
                return 1;
            }

            foreach (var failed in progress.FailedFiles)
            {
                Console.WriteLine($"  FAILED {failed}");
            }

            Console.WriteLine($"Done: {progress.Copied} copied ({progress.CopiedBytes / (1024 * 1024)} MB), " +
                              $"{progress.Skipped} already there, {progress.Failed} failed, of {progress.Total}.");
            return progress.Failed == 0 ? 0 : 1;
        }

        /// <summary>Does the copy, updating <paramref name="progress"/> as it goes. Never throws.</summary>
        public static async Task SyncAsync(
            string to, IServiceProvider services, IConfiguration configuration, StorageSyncProgress progress, CancellationToken cancellationToken)
        {
            progress.To = to;
            progress.Running = true;
            progress.StartedAtUtc = DateTime.UtcNow;
            try
            {
                S3FileStorage s3;
                try
                {
                    s3 = new S3FileStorage(configuration);
                }
                catch (Exception ex)
                {
                    progress.Error = $"S3 isn't configured on this server, so there's nothing to sync with ({ex.Message}).";
                    return;
                }

                var local = new LocalFileStorage(
                    services.GetRequiredService<IWebHostEnvironment>(), configuration, services.GetRequiredService<IHttpContextAccessor>());

                if (to == "local")
                {
                    var objects = await s3.ListAllAsync(cancellationToken);
                    progress.Total = objects.Count;
                    foreach (var (key, size) in objects)
                    {
                        await CopyOneAsync(progress, key, async () =>
                        {
                            var existing = local.ExistingFilePath(key);
                            if (existing is not null && new FileInfo(existing).Length == size)
                            {
                                return null;
                            }

                            await using var source = await s3.OpenReadAsync(key, cancellationToken)
                                ?? throw new FileNotFoundException("missing in S3");
                            return await local.SaveAsync(key, source, cancellationToken);
                        });
                    }
                }
                else
                {
                    var inBucket = (await s3.ListAllAsync(cancellationToken)).ToDictionary(o => o.Key, o => o.Size);
                    var keys = local.ListKeys().ToList();
                    progress.Total = keys.Count;
                    foreach (var key in keys)
                    {
                        await CopyOneAsync(progress, key, async () =>
                        {
                            var path = local.ExistingFilePath(key) ?? throw new FileNotFoundException("missing on disk");
                            var size = new FileInfo(path).Length;
                            if (inBucket.TryGetValue(key, out var remote) && remote == size)
                            {
                                return null;
                            }

                            await using var source = File.OpenRead(path);
                            await s3.SaveAsync(key, source, cancellationToken);
                            return size;
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                progress.Error = $"The sync stopped: {ex.Message}";
            }
            finally
            {
                progress.Running = false;
                progress.FinishedAtUtc = DateTime.UtcNow;
            }
        }

        /// <summary>Runs one file's copy; <paramref name="copy"/> returns the bytes copied, or null when it was already there.</summary>
        private static async Task CopyOneAsync(StorageSyncProgress progress, string key, Func<Task<long?>> copy)
        {
            try
            {
                var bytes = await copy();
                if (bytes is null)
                {
                    progress.Skipped++;
                }
                else
                {
                    progress.Copied++;
                    progress.CopiedBytes += bytes.Value;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                progress.Failed++;
                if (progress.FailedFiles.Count < MaxFailedFilesListed)
                {
                    progress.FailedFiles.Add($"{key}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Runs one storage sync at a time in the background (a large bucket can take a while) and keeps
    /// the latest progress for the admin screen to poll. Singleton: the state lives in this process.
    /// </summary>
    public class StorageSyncService
    {
        private readonly IServiceProvider _services;
        private readonly IConfiguration _configuration;
        private readonly object _gate = new();
        private StorageSyncProgress? _current;

        public StorageSyncService(IServiceProvider services, IConfiguration configuration)
        {
            _services = services;
            _configuration = configuration;
        }

        public StorageSyncProgress? Current => _current;

        /// <summary>Starts a sync; false when one is already running.</summary>
        public bool TryStart(string to)
        {
            lock (_gate)
            {
                if (_current is { Running: true })
                {
                    return false;
                }

                var progress = new StorageSyncProgress { To = to, Running = true, StartedAtUtc = DateTime.UtcNow };
                _current = progress;
                _ = Task.Run(() => StorageMigration.SyncAsync(to, _services, _configuration, progress, CancellationToken.None));
                return true;
            }
        }
    }
}
