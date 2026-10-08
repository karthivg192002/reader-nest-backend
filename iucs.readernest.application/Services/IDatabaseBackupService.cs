using iucs.readernest.application.Dto.Monitoring;

namespace iucs.readernest.application.Services
{
    public interface IDatabaseBackupService
    {
        /// <summary>
        /// Backup files under /var/backups/postgres (and its hourly/ sub-folder) on the app server,
        /// newest first. Throws <see cref="InvalidOperationException"/> if SSH isn't configured for it.
        /// </summary>
        Task<DatabaseBackupListDto> ListAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Copies one backup file to a local temp file and returns a read stream that deletes the
        /// temp file when disposed. <paramref name="folder"/> must be "" or "hourly" and
        /// <paramref name="fileName"/> a plain backup file name — anything else throws
        /// <see cref="ArgumentException"/> (both end up in a remote path). Records an Export audit entry.
        /// </summary>
        Task<Stream> OpenAsync(string folder, string fileName, CancellationToken cancellationToken = default);
    }
}
