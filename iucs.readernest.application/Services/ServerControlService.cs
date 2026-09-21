using iucs.readernest.application.Common.Options;
using iucs.readernest.application.Dto.Monitoring;
using Microsoft.Extensions.Options;
using Renci.SshNet;

namespace iucs.readernest.application.Services
{
    /// <summary>
    /// See IServerControlService. Same SSH.NET / whitelist pattern as ServerLogService.
    /// Each server runs its own, different Jibri autoscaler script (main: worker-first/main-fallback
    /// overflow watcher; worker: busy+1 autoscaler) -- see <see cref="MonitoredServerOptions.JibriAutoscaleScript"/>.
    /// There's no separate autoscale.conf/log on either server, so "Rescale now"/"Min warm" run the
    /// script directly over SSH and return its own real stdout as the log tail, rather than assuming
    /// an external conf/log file path that may not exist.
    /// </summary>
    public class ServerControlService : IServerControlService
    {
        private readonly MonitoringOptions _options;

        public ServerControlService(IOptions<MonitoringOptions> options)
        {
            _options = options.Value;
        }

        public async Task<JibriControlResultDto> RescaleJibriNowAsync(string serverName, CancellationToken cancellationToken = default)
        {
            var server = GetJibriCapableServer(serverName);
            using var client = Connect(server);
            try
            {
                var output = await RunAsync(client, $"bash {server.JibriAutoscaleScript}", TimeSpan.FromSeconds(30), cancellationToken);
                return new JibriControlResultDto
                {
                    Server = serverName,
                    Action = "rescale-now",
                    LogTail = TailLines(output),
                    PerformedAtUtc = DateTime.UtcNow,
                };
            }
            finally
            {
                Disconnect(client);
            }
        }

        public async Task<JibriControlResultDto> SetJibriMinReplicasAsync(string serverName, int minReplicas, CancellationToken cancellationToken = default)
        {
            var server = GetJibriCapableServer(serverName);
            if (string.IsNullOrWhiteSpace(server.JibriMinReplicasVar))
            {
                throw new InvalidOperationException($"'{serverName}' has no Jibri min-replicas variable configured.");
            }

            var clamped = Math.Clamp(minReplicas, 0, 10);

            using var client = Connect(server);
            try
            {
                // Edit the script's own MIN_* line in place, rather than an external conf file --
                // neither autoscaler script reads one. `clamped` is an int from Math.Clamp above,
                // so it's safe to splice into the sed replacement.
                await RunAsync(
                    client,
                    $"sed -i 's/^{server.JibriMinReplicasVar}=.*/{server.JibriMinReplicasVar}={clamped}/' {server.JibriAutoscaleScript}",
                    TimeSpan.FromSeconds(10),
                    cancellationToken);
                var output = await RunAsync(client, $"bash {server.JibriAutoscaleScript}", TimeSpan.FromSeconds(30), cancellationToken);
                return new JibriControlResultDto
                {
                    Server = serverName,
                    Action = $"set-min-replicas={clamped}",
                    LogTail = TailLines(output),
                    PerformedAtUtc = DateTime.UtcNow,
                };
            }
            finally
            {
                Disconnect(client);
            }
        }

        public async Task<ServiceRestartResultDto> RestartContainerAsync(string serverName, string containerName, CancellationToken cancellationToken = default)
        {
            var server = _options.Servers.FirstOrDefault(s => s.Name == serverName)
                ?? throw new ArgumentException($"Unknown server '{serverName}'.", nameof(serverName));

            if (!server.Services.Contains(containerName, StringComparer.Ordinal))
            {
                throw new ArgumentException($"'{containerName}' is not a configured service on '{serverName}'.", nameof(containerName));
            }

            EnsureSshConfigured(server);

            using var client = Connect(server);
            try
            {
                // containerName is validated against the server's own configured whitelist above.
                await RunAsync(client, $"docker restart {containerName}", TimeSpan.FromSeconds(30), cancellationToken);
                return new ServiceRestartResultDto
                {
                    Server = serverName,
                    Container = containerName,
                    PerformedAtUtc = DateTime.UtcNow,
                };
            }
            finally
            {
                Disconnect(client);
            }
        }

        private MonitoredServerOptions GetJibriCapableServer(string serverName)
        {
            var server = _options.Servers.FirstOrDefault(s => s.Name == serverName)
                ?? throw new ArgumentException($"Unknown server '{serverName}'.", nameof(serverName));

            if (string.IsNullOrWhiteSpace(server.JibriAutoscaleScript))
            {
                throw new ArgumentException($"'{serverName}' does not run a Jibri autoscaler.", nameof(serverName));
            }

            EnsureSshConfigured(server);
            return server;
        }

        private static void EnsureSshConfigured(MonitoredServerOptions server)
        {
            if (string.IsNullOrWhiteSpace(server.SshHost) || string.IsNullOrWhiteSpace(server.SshPassword))
            {
                throw new InvalidOperationException($"SSH is not configured for '{server.Name}'.");
            }
        }

        private static SshClient Connect(MonitoredServerOptions server)
        {
            var client = new SshClient(server.SshHost, server.SshPort, server.SshUsername, server.SshPassword);
            client.Connect();
            return client;
        }

        private static void Disconnect(SshClient client)
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }
            client.Dispose();
        }

        private static async Task<string> RunAsync(SshClient client, string commandText, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var command = client.CreateCommand(commandText);
            command.CommandTimeout = timeout;
            var result = await Task.Run(command.Execute, cancellationToken);
            return result + command.Error;
        }

        private static List<string> TailLines(string output)
        {
            return output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .TakeLast(10)
                .ToList();
        }
    }
}
