using iucs.readernest.application.Common.Options;
using Renci.SshNet;

namespace iucs.readernest.application.Services
{
    /// <summary>
    /// The burst worker is created, deleted and monitored by scripts living on the Jitsi/Video
    /// server ("main"), so both the usage panel and the manual Start/Stop buttons reach it the same
    /// way: SSH into main using its already-configured credentials (no extra secret to deploy).
    /// </summary>
    internal static class BurstWorkerSsh
    {
        public const string ScriptsDir = "/opt/rn-monitoring";
        public const string ScaleLog = "/var/log/burst-scale.log";

        public static MonitoredServerOptions? FindMain(MonitoringOptions options)
        {
            var main = options.Servers.FirstOrDefault(s => s.Name == "Jitsi / Video");
            return main is null || string.IsNullOrWhiteSpace(main.SshHost) || string.IsNullOrWhiteSpace(main.SshPassword)
                ? null
                : main;
        }

        public static async Task<string> RunAsync(MonitoredServerOptions main, string commandText, TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var client = new SshClient(main.SshHost, main.SshPort, main.SshUsername, main.SshPassword);
            await Task.Run(client.Connect, cancellationToken);
            try
            {
                var command = client.CreateCommand(commandText);
                command.CommandTimeout = timeout;
                var output = await Task.Run(command.Execute, cancellationToken);
                return output;
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
