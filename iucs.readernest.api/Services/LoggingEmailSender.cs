using iucs.readernest.application.Common.Interfaces;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Development transport: writes outbound mail to the log instead of sending.
    /// Swap for an SMTP/provider implementation once client mail credentials arrive.
    /// </summary>
    public class LoggingEmailSender : IEmailSender
    {
        private readonly ILogger<LoggingEmailSender> _logger;

        public LoggingEmailSender(ILogger<LoggingEmailSender> logger)
        {
            _logger = logger;
        }

        public Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation("EMAIL to {To} | {Subject}\n{Body}", toEmail, subject, body);
            return Task.CompletedTask;
        }
    }
}
