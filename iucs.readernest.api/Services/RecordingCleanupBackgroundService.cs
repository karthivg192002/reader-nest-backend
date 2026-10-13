using System.Text.RegularExpressions;
using iucs.readernest.application.Services;
using iucs.readernest.domain.Entities.Sessions;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Keeps browser-recorded classes from filling this server's disk: they are saved here rather
    /// than in S3 (see SessionsController's recording upload endpoints), about half a gigabyte per
    /// class hour, and nothing else ever removes them. Every few hours this deletes the file of
    /// any such recording an admin has deleted, and -- only when an admin has set "Delete
    /// recordings after" on Settings → Jitsi (off by default) -- removes recordings older than
    /// that many days, file and listing both.
    ///
    /// Only ever touches recordings this app's own upload path created (their link points at
    /// SessionsController.PlayRecordingFile). A Jibri recording lives on the video server under a
    /// plain URL and is left exactly as it is.
    /// </summary>
    public class RecordingCleanupBackgroundService : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly LocalFileStorage _storage;
        private readonly ILogger<RecordingCleanupBackgroundService> _logger;

        public RecordingCleanupBackgroundService(
            IServiceScopeFactory scopeFactory,
            LocalFileStorage storage,
            ILogger<RecordingCleanupBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _storage = storage;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var settings = await scope.ServiceProvider.GetRequiredService<ISessionService>().GetClassroomSettingsAsync(stoppingToken);
                    var result = await RecordingFileCleanup.RunAsync(
                        scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), _storage, settings.RecordingRetentionDays, DateTime.UtcNow, stoppingToken);
                    if (result.FilesDeleted > 0 || result.RecordingsExpired > 0)
                    {
                        _logger.LogInformation(
                            "Recording cleanup: removed {Files} file(s); {Expired} recording(s) were past the {Days}-day limit.",
                            result.FilesDeleted, result.RecordingsExpired, settings.RecordingRetentionDays);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Recording cleanup cycle failed; retrying next interval.");
                }

                await Task.Delay(Interval, stoppingToken);
            }
        }
    }

    public record RecordingCleanupResult(int FilesDeleted, int RecordingsExpired);

    public static class RecordingFileCleanup
    {
        /// <summary>The path every browser-recorded class's link contains (SessionsController.PlayRecordingFile).</summary>
        private const string LinkPath = "/api/sessions/recordings/file/";

        private static readonly Regex KeyInLink = new(@"/api/sessions/recordings/file/([0-9a-f]{32}\.(?:mp4|webm))\?", RegexOptions.Compiled);

        /// <summary>How far back an admin's own delete is still looked at, so an already-cleaned row isn't rechecked forever.</summary>
        private static readonly TimeSpan DeletedLookback = TimeSpan.FromDays(7);

        /// <param name="retentionDays">0 (or less) keeps recordings until someone deletes them.</param>
        public static async Task<RecordingCleanupResult> RunAsync(
            IUnitOfWork unitOfWork, LocalFileStorage storage, int retentionDays, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            var deletedSince = nowUtc - DeletedLookback;
            var expireBefore = nowUtc.AddDays(-retentionDays);
            var expire = retentionDays > 0;

            // Ignores the soft-delete filter on purpose: a recording an admin deleted is exactly
            // the row whose file is still sitting on disk.
            var recordings = await unitOfWork.Repository<SessionRecording>().ListForHardDeleteAsync(
                r => r.StorageUrl.Contains(LinkPath)
                     && ((r.IsDeleted && r.DeletedAtUtc >= deletedSince) || (!r.IsDeleted && expire && r.CreatedAtUtc < expireBefore)),
                cancellationToken);

            var filesDeleted = 0;
            var expiredIds = new List<Guid>();
            foreach (var recording in recordings)
            {
                var key = KeyInLink.Match(recording.StorageUrl) is { Success: true } match ? match.Groups[1].Value : null;
                if (key is not null && storage.Delete(key))
                {
                    filesDeleted++;
                }

                if (!recording.IsDeleted)
                {
                    expiredIds.Add(recording.Id);
                }
            }

            if (expiredIds.Count > 0)
            {
                var repository = unitOfWork.Repository<SessionRecording>();
                var expired = await repository.TrackedQuery().Where(r => expiredIds.Contains(r.Id)).ToListAsync(cancellationToken);
                foreach (var recording in expired)
                {
                    repository.Remove(recording);
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return new RecordingCleanupResult(filesDeleted, expiredIds.Count);
        }
    }
}
