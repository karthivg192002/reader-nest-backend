using iucs.readernest.application.Dto.Monitoring;

namespace iucs.readernest.application.Services
{
    /// <summary>
    /// The mutating counterpart to <see cref="IServerLogService"/> -- runs remote actions over
    /// SSH instead of just reading logs. Every method here changes something on production
    /// infrastructure, so each throws <see cref="ArgumentException"/> for anything not already
    /// whitelisted in that server's <see cref="Common.Options.MonitoredServerOptions"/> (never
    /// build a shell command from an unvalidated caller-supplied name), and
    /// <see cref="InvalidOperationException"/> if SSH isn't configured for the server.
    /// </summary>
    public interface IServerControlService
    {
        /// <summary>Runs the Jibri autoscaler script immediately instead of waiting out its next cron minute. Only valid for a server with TracksLiveCalls.</summary>
        Task<JibriControlResultDto> RescaleJibriNowAsync(string serverName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Sets how many Jibri instances stay warm even while idle (the one autoscaler knob that
        /// can't be safely auto-derived -- see jibri-autoscale.sh). Clamped to 1-10 regardless of
        /// the requested value; the script itself further clamps to the CPU/RAM-derived ceiling.
        /// </summary>
        Task<JibriControlResultDto> SetJibriMinReplicasAsync(string serverName, int minReplicas, CancellationToken cancellationToken = default);

        /// <summary>Restarts one configured container on one server. Whitelisted against that server's Services list -- see ServerLogService's identical safety note.</summary>
        Task<ServiceRestartResultDto> RestartContainerAsync(string serverName, string containerName, CancellationToken cancellationToken = default);
    }
}
