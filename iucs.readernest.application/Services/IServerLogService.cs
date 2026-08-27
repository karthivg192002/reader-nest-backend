using iucs.readernest.application.Dto.Monitoring;

namespace iucs.readernest.application.Services
{
    public interface IServerLogService
    {
        /// <summary>
        /// SSHes into the named server and tails `docker logs` for one of its configured
        /// containers, filtered to error-looking lines. Throws <see cref="ArgumentException"/>
        /// for an unknown server or a container not in that server's configured Services list
        /// (the whitelist this depends on for command-injection safety — see ServerLogService),
        /// and <see cref="InvalidOperationException"/> if SSH isn't configured for it.
        /// </summary>
        Task<ServerLogsDto> GetContainerErrorLogsAsync(string serverName, string containerName, int tailLines, CancellationToken cancellationToken = default);
    }
}
