using iucs.readernest.application.Common.Options;
using iucs.readernest.application.Dto.Monitoring;
using Microsoft.Extensions.Options;
using Renci.SshNet;

namespace iucs.readernest.application.Services
{
    /// <summary>See IServerControlService. Same SSH.NET / whitelist pattern as ServerLogService.</summary>
    public class ServerControlService : IServerControlService
    {
        private const string AutoscaleScriptPath = "/opt/rn-monitoring/jibri-autoscale.sh";
        private const string AutoscaleConfPath = "/opt/rn-monitoring/jibri-autoscale.conf";
        private const string AutoscaleLogPath = "/var/log/jibri-autoscale.log";

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
                await RunAsync(client, $"bash {AutoscaleScriptPath}", TimeSpan.FromSeconds(30), cancellationToken);
                var logTail = await TailLogAsync(client, cancellationToken);
                return new JibriControlResultDto
                {
                    Server = serverName,
                    Action = "rescale-now",
                    LogTail = logTail,
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
            var clamped = Math.Clamp(minReplicas, 1, 10);

            using var client = Connect(server);
            try
            {
                // Overwrite rather than edit-in-place -- the file has exactly one line and this
                // avoids any sed-quoting risk with a value that's already an int (Clamp above).
                await RunAsync(client, $"echo 'MIN_REPLICAS={clamped}' > {AutoscaleConfPath}", TimeSpan.FromSeconds(10), cancellationToken);
                await RunAsync(client, $"bash {AutoscaleScriptPath}", TimeSpan.FromSeconds(30), cancellationToken);
                var logTail = await TailLogAsync(client, cancellationToken);
                return new JibriControlResultDto
                {
                    Server = serverName,
                    Action = $"set-min-replicas={clamped}",
                    LogTail = logTail,
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

            if (!server.TracksLiveCalls)
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

        private static async Task RunAsync(SshClient client, string commandText, TimeSpan timeout, CancellationToken cancellationToken)
        {
            var command = client.CreateCommand(commandText);
            command.CommandTimeout = timeout;
            await Task.Run(command.Execute, cancellationToken);
        }

        private static async Task<List<string>> TailLogAsync(SshClient client, CancellationToken cancellationToken)
        {
            var command = client.CreateCommand($"tail -n 10 {AutoscaleLogPath} 2>/dev/null");
            command.CommandTimeout = TimeSpan.FromSeconds(10);
            var result = await Task.Run(command.Execute, cancellationToken);
            return result
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();
        }
    }
}
