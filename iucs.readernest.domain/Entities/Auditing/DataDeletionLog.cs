using System.ComponentModel.DataAnnotations;
using iucs.readernest.domain.Entities.Common;
using Microsoft.EntityFrameworkCore;

namespace iucs.readernest.domain.Entities.Auditing
{
    /// <summary>
    /// Permanent record of every row a Parent/Student hard-delete action physically removed —
    /// one row per deleted record, snapshotting its full column data before deletion. Distinct
    /// from <see cref="AuditLog"/> (which logs the single top-level "hard-deleted Parent/Student
    /// X" action, same as it logs any other admin action): this is the actual content trail, the
    /// only place that data still exists once the live row is gone for good. Never soft-deleted
    /// or edited — write-once, kept indefinitely for future reference/dispute resolution.
    /// </summary>
    [Index(nameof(DeletionBatchId))]
    [Index(nameof(RootEntityType), nameof(RootEntityId))]
    [Index(nameof(TableName), nameof(RecordId))]
    [Index(nameof(DeletedByUserId))]
    public class DataDeletionLog : BaseEntity
    {
        /// <summary>Groups every row removed by one single delete action (one Parent or one
        /// Student) together, so the whole batch can be found/replayed as a unit.</summary>
        public Guid DeletionBatchId { get; set; }

        /// <summary>"Parent" or "Student" — which delete flow triggered this row's removal.</summary>
        [MaxLength(20)]
        public string RootEntityType { get; set; } = null!;

        /// <summary>The User.Id (Parent flow) or Child.Id (Student flow) the delete was
        /// requested against — the same value for every row in one DeletionBatchId.</summary>
        public Guid RootEntityId { get; set; }

        /// <summary>The table the deleted row came from, e.g. "Invoice", "BatchEnrollment".</summary>
        [MaxLength(100)]
        public string TableName { get; set; } = null!;

        /// <summary>The deleted row's own original Id.</summary>
        public Guid RecordId { get; set; }

        /// <summary>Full column snapshot of the row as it existed immediately before deletion,
        /// serialized as JSON.</summary>
        public string DataJson { get; set; } = null!;

        public Guid DeletedByUserId { get; set; }

        // No separate DeletedAtUtc column -- BaseEntity's own CreatedAtUtc (stamped by the
        // audit interceptor the moment this log row is inserted) already IS "when this record
        // was deleted." A same-named field here would only shadow BaseEntity.DeletedAtUtc (the
        // soft-delete timestamp this row itself never gets, since it's never soft-deleted) —
        // confusing, and redundant with CreatedAtUtc.
    }
}
