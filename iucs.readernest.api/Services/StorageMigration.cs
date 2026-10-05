namespace iucs.readernest.api.Services
{
    /// <summary>
    /// One-off copy of every uploaded file between S3 and the local storage folder, keeping each
    /// file's key -- the database stores only keys, so nothing in it needs to change. Run inside the
    /// API container (it reads the same Storage:* settings the app uses; both sides must be set):
    ///
    ///   docker exec readernestbackend dotnet iucs.readernest.api.dll storage-migrate --to local
    ///   docker exec readernestbackend dotnet iucs.readernest.api.dll storage-migrate --to s3
    ///
    /// Safe to re-run: files already present with the same size are skipped, and nothing is ever
    /// deleted from the source. Switch Storage:Provider only after it reports no failures.
    /// </summary>
    public static class StorageMigration
    {
        public static bool IsRequested(string[] args) =>
            args.Length > 0 && string.Equals(args[0], "storage-migrate", StringComparison.OrdinalIgnoreCase);

        public static async Task<int> RunAsync(string[] args, IServiceProvider services, IConfiguration configuration)
        {
            var to = args.SkipWhile(a => !string.Equals(a, "--to", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault()?.ToLowerInvariant();
            if (to is not ("local" or "s3"))
            {
                Console.WriteLine("Usage: storage-migrate --to local   (copy every S3 file into Storage:LocalPath)");
                Console.WriteLine("       storage-migrate --to s3      (copy every local file into the S3 bucket)");
                return 2;
            }

            var s3 = new S3FileStorage(configuration);
            var local = new LocalFileStorage(
                services.GetRequiredService<IWebHostEnvironment>(), configuration, services.GetRequiredService<IHttpContextAccessor>());
            Console.WriteLine($"Local folder: {local.RootPath}");

            int copied = 0, skipped = 0, failed = 0;
            long bytes = 0;
            if (to == "local")
            {
                var objects = await s3.ListAllAsync();
                Console.WriteLine($"S3 bucket has {objects.Count} file(s). Copying to the local folder...");
                foreach (var (key, size) in objects)
                {
                    try
                    {
                        var existing = local.ExistingFilePath(key);
                        if (existing is not null && new FileInfo(existing).Length == size)
                        {
                            skipped++;
                            continue;
                        }

                        await using var source = await s3.OpenReadAsync(key)
                            ?? throw new FileNotFoundException("missing in S3", key);
                        bytes += await local.SaveAsync(key, source);
                        copied++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Console.WriteLine($"  FAILED {key}: {ex.Message}");
                    }
                }
            }
            else
            {
                var inBucket = (await s3.ListAllAsync()).ToDictionary(o => o.Key, o => o.Size);
                var keys = local.ListKeys().ToList();
                Console.WriteLine($"Local folder has {keys.Count} file(s). Copying to the S3 bucket...");
                foreach (var key in keys)
                {
                    try
                    {
                        var path = local.ExistingFilePath(key)!;
                        var size = new FileInfo(path).Length;
                        if (inBucket.TryGetValue(key, out var remote) && remote == size)
                        {
                            skipped++;
                            continue;
                        }

                        await using var source = File.OpenRead(path);
                        await s3.SaveAsync(key, source);
                        bytes += size;
                        copied++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Console.WriteLine($"  FAILED {key}: {ex.Message}");
                    }
                }
            }

            Console.WriteLine($"Done: {copied} copied ({bytes / (1024 * 1024)} MB), {skipped} already there, {failed} failed.");
            return failed == 0 ? 0 : 1;
        }
    }
}
