using System.Globalization;
using System.Text.RegularExpressions;
using iucs.readernest.application.Common.Options;
using iucs.readernest.application.Dto.Monitoring;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.Extensions.Options;
using Renci.SshNet;

namespace iucs.readernest.application.Services
{
    /// <summary>
    /// Lists and downloads the PostgreSQL backups pg_backup.sh (nightly) and pg_backup_hourly.sh
    /// (hourly) leave on the app server, for the Monitoring page's "Database backups" panel —
    /// added after the 7 Oct 2026 incident, when the only way to get a backup off the server was
    /// a root shell. Same SSH credentials as ServerLogService. Folder and file name both end up in
    /// a remote path, so they're validated against a fixed folder list and a strict name pattern;
    /// never relax either.
    /// </summary>
    public class DatabaseBackupService : IDatabaseBackupService
    {
        public const string BackupRoot = "/var/backups/postgres";

        private static readonly Regex FileNamePattern = new(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,199}\.(dump|sql\.gz)$");
        // pg_backup.sh names its files DBNAME_yyyyMMdd_HHmmss.dump; anything else in the root folder was made by hand.
        private static readonly Regex NightlyPattern = new(@"^[a-z0-9_]+_\d{8}_\d{6}\.dump$");

        private readonly MonitoringOptions _options;
        private readonly IAuditLogService _auditLog;
        private readonly IUnitOfWork _unitOfWork;

        public DatabaseBackupService(IOptions<MonitoringOptions> options, IAuditLogService auditLog, IUnitOfWork unitOfWork)
        {
            _options = options.Value;
            _auditLog = auditLog;
            _unitOfWork = unitOfWork;
        }

        public async Task<DatabaseBackupListDto> ListAsync(CancellationToken cancellationToken = default)
        {
            var server = BackupServer();
            var command = $"find {BackupRoot} -maxdepth 2 -type f \\( -name '*.dump' -o -name '*.sql.gz' \\) -printf '%h|%f|%s|%T@\\n' 2>/dev/null";

            using var client = new SshClient(server.SshHost, server.SshPort, server.SshUsername, server.SshPassword);
            await Task.Run(client.Connect, cancellationToken);
            try
            {
                var cmd = client.CreateCommand(command);
                cmd.CommandTimeout = TimeSpan.FromSeconds(15);
                var output = await Task.Run(cmd.Execute, cancellationToken);
                var backups = ParseFindOutput(output);
                return new DatabaseBackupListDto
                {
                    Server = server.Name,
                    Backups = backups,
                    NewestScheduledAtUtc = backups.Where(b => b.Kind != "manual").Select(b => (DateTime?)b.CreatedAtUtc).FirstOrDefault(),
                    FetchedAtUtc = DateTime.UtcNow,
                };
            }
            finally
            {
                if (client.IsConnected)
                {
                    client.Disconnect();
                }
            }
        }

        public async Task<Stream> OpenAsync(string folder, string fileName, CancellationToken cancellationToken = default)
        {
            var remotePath = RemotePath(folder, fileName);
            var server = BackupServer();

            var tempPath = Path.Combine(Path.GetTempPath(), $"rn-backup-{Guid.NewGuid():N}.tmp");
            using (var client = new SftpClient(server.SshHost, server.SshPort, server.SshUsername, server.SshPassword))
            {
                await Task.Run(client.Connect, cancellationToken);
                try
                {
                    if (!client.Exists(remotePath))
                    {
                        throw new ArgumentException($"Backup '{fileName}' no longer exists (it may have been rotated out).", nameof(fileName));
                    }
                    await using var local = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write);
                    await Task.Run(() => client.DownloadFile(remotePath, local), cancellationToken);
                }
                catch
                {
                    File.Delete(tempPath);
                    throw;
                }
                finally
                {
                    if (client.IsConnected)
                    {
                        client.Disconnect();
                    }
                }
            }

            // A full database dump carries every user's personal data — who took one off the server is audit-worthy.
            await _auditLog.StageAsync(AuditAction.Export, "DatabaseBackup", remotePath, cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            return new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.None, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        }

        /// <summary>Absolute remote path for a validated folder + file name; throws <see cref="ArgumentException"/> otherwise.</summary>
        public static string RemotePath(string? folder, string? fileName)
        {
            folder ??= string.Empty;
            if (folder is not ("" or "hourly"))
            {
                throw new ArgumentException("Folder must be empty or 'hourly'.", nameof(folder));
            }
            if (fileName is null || !FileNamePattern.IsMatch(fileName) || fileName.Contains("..", StringComparison.Ordinal))
            {
                throw new ArgumentException("Not a backup file name.", nameof(fileName));
            }
            return folder.Length == 0 ? $"{BackupRoot}/{fileName}" : $"{BackupRoot}/{folder}/{fileName}";
        }

        /// <summary>Parses `find -printf '%h|%f|%s|%T@'` lines into backups, newest first. Lines outside the two known folders or with odd names are skipped.</summary>
        public static List<DatabaseBackupDto> ParseFindOutput(string output)
        {
            var backups = new List<DatabaseBackupDto>();
            foreach (var raw in output.Split('\n'))
            {
                var parts = raw.TrimEnd('\r').Split('|');
                if (parts.Length != 4 || !FileNamePattern.IsMatch(parts[1]))
                {
                    continue;
                }
                var folder = parts[0] == BackupRoot ? "" : parts[0] == $"{BackupRoot}/hourly" ? "hourly" : null;
                if (folder is null
                    || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var size)
                    || !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var epoch))
                {
                    continue;
                }
                var name = parts[1];
                var kind = folder == "hourly"
                    ? (name.Contains("BEFORE", StringComparison.Ordinal) ? "manual" : "hourly")
                    : (NightlyPattern.IsMatch(name) ? "nightly" : "manual");
                backups.Add(new DatabaseBackupDto
                {
                    Kind = kind,
                    Folder = folder,
                    FileName = name,
                    SizeBytes = size,
                    CreatedAtUtc = DateTime.UnixEpoch.AddSeconds(epoch),
                });
            }
            return backups.OrderByDescending(b => b.CreatedAtUtc).ToList();
        }

        private MonitoredServerOptions BackupServer()
        {
            var server = _options.Servers.FirstOrDefault(s => s.Name == _options.BackupServerName)
                ?? throw new InvalidOperationException($"Backup server '{_options.BackupServerName}' is not in Monitoring:Servers.");
            if (string.IsNullOrWhiteSpace(server.SshHost) || string.IsNullOrWhiteSpace(server.SshPassword))
            {
                throw new InvalidOperationException($"SSH is not configured for '{server.Name}'.");
            }
            return server;
        }
    }
}
