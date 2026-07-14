using System.Text.Json;
using iucs.readernest.application.Common.Exceptions;
using iucs.readernest.application.Dto.Integrations;
using iucs.readernest.domain.Entities.Integrations;
using iucs.readernest.domain.Enums;
using iucs.readernest.domain.Repository;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.application.Services
{
    public class IntegrationService : IIntegrationService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly IAuditLogService _auditLog;

        public IntegrationService(IUnitOfWork unitOfWork, IAuditLogService auditLog)
        {
            _unitOfWork = unitOfWork;
            _auditLog = auditLog;
        }

        public async Task<IReadOnlyList<IntegrationDto>> ListAsync(CancellationToken cancellationToken = default)
        {
            var integrations = await _unitOfWork.Repository<Integration>().Query()
                .OrderBy(i => i.Category).ThenBy(i => i.Name)
                .ToListAsync(cancellationToken);

            return integrations.Select(ToDto).ToList();
        }

        public async Task<IntegrationDto> CreateAsync(
            SaveIntegrationRequest request,
            CancellationToken cancellationToken = default)
        {
            var key = NormalizeKey(request.Key);
            ValidateName(request.Name);

            var repository = _unitOfWork.Repository<Integration>();
            if (await repository.ExistsAsync(i => i.Key == key, cancellationToken))
            {
                throw new ConflictException($"An integration with key '{key}' already exists.");
            }

            var integration = new Integration();
            Apply(integration, request, key, request.Config);
            await repository.AddAsync(integration, cancellationToken);

            await _auditLog.StageAsync(AuditAction.Create, nameof(Integration), key, cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(integration);
        }

        public async Task<IntegrationDto> UpdateAsync(
            Guid id,
            SaveIntegrationRequest request,
            CancellationToken cancellationToken = default)
        {
            var key = NormalizeKey(request.Key);
            ValidateName(request.Name);

            var repository = _unitOfWork.Repository<Integration>();
            var integration = await repository.GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(Integration), id);

            if (integration.IsSystem && integration.Key != key)
            {
                throw new DomainValidationException($"System integration '{integration.Key}' cannot be re-keyed.");
            }

            if (await repository.ExistsAsync(i => i.Id != id && i.Key == key, cancellationToken))
            {
                throw new ConflictException($"An integration with key '{key}' already exists.");
            }

            // The client only ever sees masked secrets back; if a field still holds
            // its mask placeholder the admin didn't touch it, so keep the real value.
            var existingConfig = DecodeConfig(integration.ConfigJson);
            var resolvedConfig = request.Config.ToDictionary(
                kv => kv.Key,
                kv => IsSecretField(kv.Key) && existingConfig.TryGetValue(kv.Key, out var current) && kv.Value == Mask(current)
                    ? current
                    : kv.Value);

            Apply(integration, request, key, resolvedConfig);
            repository.Update(integration);

            await _auditLog.StageAsync(AuditAction.Update, nameof(Integration), key, cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            return ToDto(integration);
        }

        public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var repository = _unitOfWork.Repository<Integration>();
            var integration = await repository.GetByIdAsync(id, cancellationToken)
                ?? throw new NotFoundException(nameof(Integration), id);

            if (integration.IsSystem)
            {
                throw new DomainValidationException($"System integration '{integration.Key}' cannot be deleted.");
            }

            repository.Remove(integration);
            await _auditLog.StageAsync(AuditAction.Delete, nameof(Integration), integration.Key, cancellationToken: cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        private static string NormalizeKey(string key)
        {
            var normalized = key?.Trim().ToLowerInvariant() ?? string.Empty;
            if (normalized.Length == 0)
            {
                throw new DomainValidationException("Integration key is required.");
            }

            return normalized;
        }

        private static void ValidateName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new DomainValidationException("Integration name is required.");
            }
        }

        private static void Apply(Integration integration, SaveIntegrationRequest request, string key, Dictionary<string, string?> config)
        {
            integration.Key = key;
            integration.Name = request.Name.Trim();
            integration.Category = request.Category;
            integration.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            integration.IsEnabled = request.IsEnabled;
            integration.ConfigJson = config.Count > 0 ? JsonSerializer.Serialize(config) : null;
        }

        private static Dictionary<string, string?> DecodeConfig(string? configJson)
        {
            return string.IsNullOrWhiteSpace(configJson)
                ? []
                : JsonSerializer.Deserialize<Dictionary<string, string?>>(configJson) ?? [];
        }

        /// <summary>Field names that hold credentials/secrets and must never round-trip to the client in the clear.</summary>
        private static readonly string[] SecretFieldHints = ["secret", "key", "token", "password"];

        private static bool IsSecretField(string fieldName) =>
            SecretFieldHints.Any(hint => fieldName.Contains(hint, StringComparison.OrdinalIgnoreCase));

        /// <summary>Masks all but the last 4 characters, e.g. "sk_live_abcd1234" -> "••••••••••••1234".</summary>
        private static string? Mask(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return value.Length <= 4
                ? new string('•', value.Length)
                : new string('•', value.Length - 4) + value[^4..];
        }

        private static IntegrationDto ToDto(Integration integration)
        {
            var config = DecodeConfig(integration.ConfigJson);
            return new IntegrationDto
            {
                Id = integration.Id,
                Key = integration.Key,
                Name = integration.Name,
                Category = integration.Category,
                Description = integration.Description,
                IsEnabled = integration.IsEnabled,
                Config = config.ToDictionary(kv => kv.Key, kv => IsSecretField(kv.Key) ? Mask(kv.Value) : kv.Value),
                IsSystem = integration.IsSystem,
            };
        }
    }
}
