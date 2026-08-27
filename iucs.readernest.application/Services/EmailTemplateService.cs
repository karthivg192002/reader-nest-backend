using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using iucs.readernest.application.Common;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Communication;
using iucs.readernest.domain.Entities.Communication;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace iucs.readernest.application.Services
{
    public class EmailTemplateService : IEmailTemplateService
    {
        private const string CacheKeyPrefix = "email-template:";

        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;
        private readonly IMemoryCache _cache;

        public EmailTemplateService(IUnitOfWork unitOfWork, IAuditLogService auditLog, IMemoryCache cache)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
            _cache = cache;
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
            _cache.Remove(CacheKeyPrefix + template.Key);
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
            // Bulk sends (e.g. a class-summary email to every parent in a batch) call this
            // once per recipient with the same key — a fresh SELECT per call was a real N+1.
            // Cached as a plain snapshot (never the tracked entity: it belongs to this
            // request's DbContext and must not be reused once that's disposed), invalidated
            // by UpdateAsync so an edit takes effect immediately rather than waiting out the TTL.
            var snapshot = await _cache.GetOrCreateAsync(CacheKeyPrefix + key, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                var template = await _unitOfWork.Repository<EmailTemplate>().Query()
                    .FirstOrDefaultAsync(t => t.Key == key, cancellationToken);
                return template is null
                    ? null
                    : new TemplateSnapshot(template.Subject, template.HtmlBody, template.IsActive);
            });

            var brandName = await BrandSettings.GetNameAsync(_unitOfWork, cancellationToken);

            if (snapshot is null || !snapshot.IsActive)
            {
                // A missing/disabled template must never block the underlying business
                // operation — fall back to a minimal generic message instead of throwing.
                var fallbackSubject = tokens.TryGetValue("Subject", out var s) && !string.IsNullOrWhiteSpace(s)
                    ? s
                    : $"Notification from {brandName}";
                return (fallbackSubject, $"<p>Please check your {brandName} dashboard for details.</p>");
            }

            // Every template can use {{OrgName}} without its caller having to know to pass
            // one — templates used to hardcode "The Reader Nest" directly (white-labeling
            // gap: see docs/WHITE_LABEL_BRANDING.md's "Product naming" row), so this is
            // injected here, centrally, rather than at each of the many call sites.
            // A caller-supplied "OrgName" (none exist today) still wins over this default.
            var withOrgName = tokens.ContainsKey("OrgName")
                ? tokens
                : new Dictionary<string, string>(tokens) { ["OrgName"] = brandName };

            return (SubstituteSubject(snapshot.Subject, withOrgName), SubstituteHtml(snapshot.HtmlBody, withOrgName));
        }

        private sealed record TemplateSnapshot(string Subject, string HtmlBody, bool IsActive);

        // Single-pass placeholder match: each {{Token}} in the ORIGINAL template is
        // matched exactly once and replaced via a MatchEvaluator, whose return value is
        // never re-scanned. A sequential Replace-per-token loop (the previous
        // implementation) would let one token's substituted value — if it happened to
        // contain a literal "{{OtherToken}}" — get matched and replaced all over again
        // by a later iteration, substituting content into a spot nothing in the template
        // ever placed there.
        private static readonly Regex TokenPattern = new(@"\{\{(\w+)\}\}", RegexOptions.Compiled);

        private static string SubstituteSubject(string subject, IReadOnlyDictionary<string, string> tokens)
        {
            return TokenPattern.Replace(subject, match =>
            {
                if (!tokens.TryGetValue(match.Groups[1].Value, out var value))
                {
                    return match.Value;
                }

                // Subject is a plain-text mail header — strip line breaks so a token value
                // can never smuggle extra headers in (header injection), but no HTML escaping.
                return (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ");
            });
        }

        private static string SubstituteHtml(string html, IReadOnlyDictionary<string, string> tokens)
        {
            return TokenPattern.Replace(html, match =>
            {
                if (!tokens.TryGetValue(match.Groups[1].Value, out var value))
                {
                    return match.Value;
                }

                return WebUtility.HtmlEncode(value ?? string.Empty).Replace("\n", "<br/>");
            });
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
