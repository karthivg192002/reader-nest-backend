using System.Net;
using System.Text.Json;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Communication;
using iucs.readernest.domain.Entities.Communication;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class EmailTemplateService : IEmailTemplateService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;

        public EmailTemplateService(IUnitOfWork unitOfWork, IAuditLogService auditLog)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
        }

        public async Task<IReadOnlyList<EmailTemplateDto>> ListAsync(CancellationToken cancellationToken = default)
        {
            var templates = await _unitOfWork.Repository<EmailTemplate>().Query()
                .OrderBy(t => t.Category).ThenBy(t => t.Name)
                .ToListAsync(cancellationToken);

            return templates.Select(ToDto).ToList();
        }

        public async Task<EmailTemplateDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var template = await _unitOfWork.Repository<EmailTemplate>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(EmailTemplate), id);
            return ToDto(template);
        }

        public async Task<EmailTemplateDto> UpdateAsync(
            Guid id,
            SaveEmailTemplateRequest request,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.Subject))
            {
                throw new DomainValidationException("Subject is required.");
            }
            if (string.IsNullOrWhiteSpace(request.HtmlBody))
            {
                throw new DomainValidationException("Email body is required.");
            }

            var repository = _unitOfWork.Repository<EmailTemplate>();
            var template = await repository.GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(EmailTemplate), id);

            template.Subject = request.Subject.Trim();
            template.HtmlBody = request.HtmlBody;
            template.IsActive = request.IsActive;
            repository.Update(template);

            await _auditLog.StageAsync(AuditAction.Update, nameof(EmailTemplate), template.Key, cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(template);
        }

        public async Task<EmailTemplatePreviewDto> PreviewAsync(
            Guid id,
            PreviewEmailTemplateRequest request,
            CancellationToken cancellationToken = default)
        {
            var template = await _unitOfWork.Repository<EmailTemplate>().GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(EmailTemplate), id);

            var placeholders = DecodePlaceholders(template.PlaceholdersJson);
            var tokens = placeholders.ToDictionary(
                p => p,
                p => request.SampleTokens.TryGetValue(p, out var v) && !string.IsNullOrWhiteSpace(v) ? v : $"[{p}]");

            return new EmailTemplatePreviewDto
            {
                Subject = SubstituteSubject(template.Subject, tokens),
                HtmlBody = SubstituteHtml(template.HtmlBody, tokens),
            };
        }

        public async Task<(string Subject, string HtmlBody)> RenderAsync(
            string key,
            IReadOnlyDictionary<string, string> tokens,
            CancellationToken cancellationToken = default)
        {
            var template = await _unitOfWork.Repository<EmailTemplate>().Query()
                .FirstOrDefaultAsync(t => t.Key == key, cancellationToken);

            if (template is null || !template.IsActive)
            {
                // A missing/disabled template must never block the underlying business
                // operation — fall back to a minimal generic message instead of throwing.
                var fallbackSubject = tokens.TryGetValue("Subject", out var s) && !string.IsNullOrWhiteSpace(s)
                    ? s
                    : "Notification from The Reader Nest";
                return (fallbackSubject, "<p>Please check your Reader Nest dashboard for details.</p>");
            }

            return (SubstituteSubject(template.Subject, tokens), SubstituteHtml(template.HtmlBody, tokens));
        }

        private static string SubstituteSubject(string subject, IReadOnlyDictionary<string, string> tokens)
        {
            foreach (var (key, value) in tokens)
            {
                // Subject is a plain-text mail header — strip line breaks so a token value
                // can never smuggle extra headers in (header injection), but no HTML escaping.
                var safe = (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
                subject = subject.Replace("{{" + key + "}}", safe);
            }
            return subject;
        }

        private static string SubstituteHtml(string html, IReadOnlyDictionary<string, string> tokens)
        {
            foreach (var (key, value) in tokens)
            {
                var safe = WebUtility.HtmlEncode(value ?? string.Empty).Replace("\n", "<br/>");
                html = html.Replace("{{" + key + "}}", safe);
            }
            return html;
        }

        private static IReadOnlyList<string> DecodePlaceholders(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<string>>(json) ?? [];
        }

        private static EmailTemplateDto ToDto(EmailTemplate template) => new()
        {
            Id = template.Id,
            Key = template.Key,
            Name = template.Name,
            Description = template.Description,
            Category = template.Category,
            Subject = template.Subject,
            HtmlBody = template.HtmlBody,
            Placeholders = DecodePlaceholders(template.PlaceholdersJson),
            IsActive = template.IsActive,
            IsSystem = template.IsSystem,
            UpdatedAtUtc = template.UpdatedAtUtc,
        };
    }
}
