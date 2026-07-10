using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Services
{
    /// <summary>
    /// Appends rows to the audit trail. Callers stage the entry; it is persisted
    /// with the caller's SaveChangesAsync so the action and its audit line commit together.
    /// </summary>
    public interface IAuditLogService
    {
        Task StageAsync(
            AuditAction action,
            string entityName,
            string? entityId = null,
            string? changesJson = null,
            CancellationToken cancellationToken = default);
    }
}
