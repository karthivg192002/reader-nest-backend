using iucs.readernest.domain.Enums;

namespace iucs.readernest.application.Dto.Audit
{
    /// <summary>One audit-trail row, resolved with the actor's display name for the Audit Log screen.</summary>
    public class AuditLogDto
    {
        public Guid Id { get; set; }

        public Guid? ActorUserId { get; set; }

        public string? ActorName { get; set; }

        public AuditAction Action { get; set; }

        public string EntityName { get; set; } = null!;

        public string? EntityId { get; set; }

        public string? ChangesJson { get; set; }

        public DateTime CreatedAtUtc { get; set; }
    }
}
