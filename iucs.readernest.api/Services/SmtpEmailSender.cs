using System.Net;
using System.Net.Mail;
using System.Text.Json;
using iucs.readernest.application.Common.Interfaces;
using iucs.readernest.domain.Entities.Integrations;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.api.Services
{
    /// <summary>
    /// Sends real email over SMTP using the credentials the admin configured in
    /// Settings → Integrations (the "email" record). Reads config live from the DB
    /// so a rebranding/mail-account change needs no redeploy. Falls back to logging
    /// (and never throws) when the integration is disabled or unconfigured, so
    /// account creation and other flows keep working in dev without a mail server.
    /// </summary>
    public class SmtpEmailSender : IEmailSender
    {
        private const string EmailIntegrationKey = "email";

        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(IUnitOfWork unitOfWork, ILogger<SmtpEmailSender> logger)
        {
            _unitOfWork = unitOfWork;
            _logger = logger;
        }

        public async Task SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken = default)
        {
            var integration = await _unitOfWork.Repository<Integration>().Query()
                .FirstOrDefaultAsync(i => i.Key == EmailIntegrationKey, cancellationToken);

            var config = DecodeConfig(integration?.ConfigJson);
            var host = Value(config, "smtpHost");

            if (integration is null || !integration.IsEnabled || string.IsNullOrWhiteSpace(host))
            {
                _logger.LogInformation(
                    "EMAIL (not sent — SMTP integration disabled or unconfigured) to {To} | {Subject}\n{Body}",
                    toEmail, subject, body);
                return;
            }

            var fromAddress = Value(config, "fromAddress") ?? Value(config, "username") ?? "no-reply@thereadernest.com";
            var username = Value(config, "username");
            var password = Value(config, "password");
            var port = int.TryParse(Value(config, "smtpPort"), out var parsedPort) ? parsedPort : 587;
            // Default to TLS on; only an explicit "false" disables it.
            var useTls = !string.Equals(Value(config, "use_tls"), "false", StringComparison.OrdinalIgnoreCase);

            using var message = new MailMessage(fromAddress, toEmail, subject, body) { IsBodyHtml = true };
            using var client = new SmtpClient(host, port)
            {
                EnableSsl = useTls,
                Timeout = 15000,
            };
            if (!string.IsNullOrWhiteSpace(username))
            {
                client.Credentials = new NetworkCredential(username, password);
            }

            await client.SendMailAsync(message, cancellationToken);
            _logger.LogInformation("EMAIL sent to {To} via {Host}:{Port} | {Subject}", toEmail, host, port, subject);
        }

        private static Dictionary<string, string?> DecodeConfig(string? configJson) =>
            string.IsNullOrWhiteSpace(configJson)
                ? new Dictionary<string, string?>()
                : JsonSerializer.Deserialize<Dictionary<string, string?>>(configJson) ?? new Dictionary<string, string?>();

        private static string? Value(Dictionary<string, string?> config, string key) =>
            config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }
}
