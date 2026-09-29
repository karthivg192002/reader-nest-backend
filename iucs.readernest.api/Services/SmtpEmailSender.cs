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
            // A parent with no email (demo booked by phone, or a no-email account's internal
            // login key) has nothing to deliver to — skip quietly rather than bounce or fail the
            // caller's own action. They're reached on WhatsApp instead.
            if (!application.Common.ParentLogin.IsDeliverable(toEmail))
            {
                return;
            }

            var integration = await _unitOfWork.Repository<Integration>().Query()
                .FirstOrDefaultAsync(i => i.Key == EmailIntegrationKey, cancellationToken);

            var config = DecodeConfig(integration?.ConfigJson);
            var host = Value(config, "smtpHost");

            if (integration is null || !integration.IsEnabled || string.IsNullOrWhiteSpace(host))
            {
                // Never log the rendered body: it routinely carries a temporary password
                // (welcome-credentials), review notes, or other content not meant for the
                // log aggregator — subject/recipient is enough to see delivery was skipped.
                _logger.LogInformation(
                    "EMAIL (not sent — SMTP integration disabled or unconfigured) to {To} | {Subject}",
                    toEmail, subject);
                return;
            }

            var fromAddress = Value(config, "fromAddress") ?? Value(config, "username") ?? "no-reply@meettomanage.cloud";
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

        /// <summary>
        /// Settings → "Send test email": the same delivery as SendAsync, but it reports every problem
        /// instead of quietly skipping. Reported live: parents never received their PIN-reset email,
        /// and nothing showed why — SendAsync deliberately never fails (a disabled or half-configured
        /// integration is only logged), so the email log still said "sent". Returns null on success,
        /// else a plain explanation of what's wrong.
        /// </summary>
        public async Task<string?> SendTestAsync(string toEmail, CancellationToken cancellationToken = default)
        {
            var integration = await _unitOfWork.Repository<Integration>().Query()
                .FirstOrDefaultAsync(i => i.Key == EmailIntegrationKey, cancellationToken);
            if (integration is null)
            {
                return "There is no Email integration set up. Add it in Settings → Integrations.";
            }
            if (!integration.IsEnabled)
            {
                return "The Email integration is turned off, so no email is being sent (including PIN resets). Turn it on in Settings → Integrations.";
            }

            var config = DecodeConfig(integration.ConfigJson);
            var host = Value(config, "smtpHost");
            if (string.IsNullOrWhiteSpace(host))
            {
                return "The Email integration has no SMTP host, so no email is being sent. Fill in the SMTP settings.";
            }
            if (Value(config, "fromAddress") is null)
            {
                return "No \"From\" address is set, so emails go out from no-reply@meettomanage.cloud, which Gmail and others often reject or send to spam. Set a From address on your own domain.";
            }

            try
            {
                await SendAsync(toEmail, "The Reader Nest — test email",
                    "<p>This is a test email from The Reader Nest portal. If you can read this, email delivery is working.</p>",
                    cancellationToken);
                return null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Test email to {To} failed", toEmail);
                return $"The mail server refused the email: {ex.GetBaseException().Message}";
            }
        }

        private static Dictionary<string, string?> DecodeConfig(string? configJson) =>
            string.IsNullOrWhiteSpace(configJson)
                ? new Dictionary<string, string?>()
                : JsonSerializer.Deserialize<Dictionary<string, string?>>(configJson) ?? new Dictionary<string, string?>();

        private static string? Value(Dictionary<string, string?> config, string key) =>
            config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;
    }
}
