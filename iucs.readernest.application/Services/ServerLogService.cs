using iucs.readernest.application.Common.Options;
using iucs.readernest.application.Dto.Monitoring;
using Microsoft.Extensions.Options;
using Renci.SshNet;

namespace iucs.readernest.application.Services
{
    /// <summary>
    /// Fetches error-filtered `docker logs` over SSH for one container on one monitored server.
    /// This is the one place in the app that runs a remote shell command, so the whitelist check
    /// below (containerName must already be in that server's configured Services list) is load-
    /// bearing: it's what stops an otherwise-authorized caller from turning the "which container"
    /// parameter into an arbitrary shell command against production infrastructure.
    /// </summary>
    public class ServerLogService : IServerLogService
    {
        private readonly MonitoringOptions _options;

        public ServerLogService(IOptions<MonitoringOptions> options)
        {
            _options = options.Value;
        }

        public async Task<ServerLogsDto> GetContainerErrorLogsAsync(string serverName, string containerName, int tailLines, CancellationToken cancellationToken = default)
        {
            var server = _options.Servers.FirstOrDefault(s => s.Name == serverName)
                ?? throw new ArgumentException($"Unknown server '{serverName}'.", nameof(serverName));

            if (!server.Services.Contains(containerName, StringComparer.Ordinal))
            {
                throw new ArgumentException($"'{containerName}' is not a configured service on '{serverName}'.", nameof(containerName));
            }

            if (string.IsNullOrWhiteSpace(server.SshHost) || string.IsNullOrWhiteSpace(server.SshPassword))
            {
                throw new InvalidOperationException($"SSH is not configured for '{serverName}'.");
            }

            var clampedLines = Math.Clamp(tailLines, 10, 1000);
            var logsCommand = $"docker logs --tail {clampedLines} --timestamps {containerName} 2>&1 | grep -iE 'error|exception|fatal|fail' | tail -n 150";

            // The burst-worker's only route from this server is through main's WireGuard tunnel
            // (10.10.10.3) -- main itself already has a key-based hop to it (set up for its own
            // idle-check in burst-scale-down.sh), so we reuse that instead of standing up a
            // second, separate credential path. Connect to the jump host and run a nested ssh
            // rather than connecting to server.SshHost directly.
            var usingProxy = !string.IsNullOrWhiteSpace(server.SshProxyHost);
            var connectHost = usingProxy ? server.SshProxyHost : server.SshHost;
            var connectPort = usingProxy ? server.SshProxyPort : server.SshPort;
            var connectUsername = usingProxy ? server.SshProxyUsername : server.SshUsername;
            var connectPassword = usingProxy ? server.SshProxyPassword : server.SshPassword;
            var remoteCommand = usingProxy
                ? $"ssh -o ConnectTimeout=8 -o StrictHostKeyChecking=accept-new -i /root/.ssh/id_ed25519 root@{server.SshHost} \"{logsCommand}\""
                : logsCommand;

            using var client = new SshClient(connectHost, connectPort, connectUsername, connectPassword);
            await Task.Run(client.Connect, cancellationToken);
            try
            {
                // containerName is validated against the server's own configured whitelist above,
                // so interpolating it here (directly or inside the nested ssh command above) is
                // safe -- never do this with an unvalidated name.
                var command = client.CreateCommand(remoteCommand);
                command.CommandTimeout = TimeSpan.FromSeconds(usingProxy ? 20 : 15);
                var result = await Task.Run(command.Execute, cancellationToken);

                if (usingProxy && string.IsNullOrWhiteSpace(result) && !string.IsNullOrWhiteSpace(command.Error)
                    && command.Error.Contains("Connection timed out", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"'{serverName}' is not currently running (on-demand server).");
                }

                var lines = result
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();

                return new ServerLogsDto
                {
                    Server = serverName,
                    Container = containerName,
                    Lines = lines,
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
    }
}
