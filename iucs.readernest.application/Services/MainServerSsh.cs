using iucs.readernest.application.Common.Options;
using Renci.SshNet;

namespace iucs.readernest.application.Services
{
    /// <summary>
    /// Runs a read-only command on the Jitsi/Video server ("main") over SSH, using its already
    /// configured Monitoring credentials (no extra secret to deploy). Used by the dashboard panels
    /// that read main's own logs: call-quality incidents and the recording pipeline.
    /// </summary>
    internal static class MainServerSsh
    {
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
