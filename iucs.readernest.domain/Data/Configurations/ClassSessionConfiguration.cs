using iucs.readernest.domain.Entities.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace iucs.readernest.domain.Data.Configurations
{
    public class ClassSessionConfiguration : IEntityTypeConfiguration<ClassSession>
    {
        public void Configure(EntityTypeBuilder<ClassSession> builder)
        {
            // Two independent self-references; without explicit config EF pairs
            // them as inverse ends of a single one-to-one relationship.
            builder.HasOne(s => s.RescheduledFromSession)
                .WithMany()
                .HasForeignKey(s => s.RescheduledFromSessionId);

            builder.HasOne(s => s.CarriedForwardFromSession)
                .WithMany()
                .HasForeignKey(s => s.CarriedForwardFromSessionId);

            // Replaces the old plain (non-unique) index on the same columns. Enforced in the
            // database rather than just checked in GenerateScheduleAsync, because a check-then-
            // insert in application code can't stop two near-simultaneous requests from both
            // passing the "does this batch already have sessions" check before either commits —
            // that race is exactly what produced 91 duplicated sessions across 2026-09
            // (see docs/maintenance/hard-delete-migration-duplicates-2026-09-17.sql and
            // hard-delete-manual-review-2026-09-18.sql). Partial (WHERE is_deleted = false AND
            // status <> 'Cancelled') so a soft-deleted OR cancelled slot doesn't block a
            // legitimate new one at the same time — without the status exclusion,
            // SessionService.UpdateFutureScheduleAsync's "regenerate remaining sessions" path
            // (which cancels the old sessions rather than deleting them, to keep history/audit
            // intact) hit this constraint and crashed with an unhandled 500 every time the new
            // weekday pattern kept a day the old one already had, because the freshly-placed
            // session landed on the exact same (batch, start time) as the cancelled one it was
            // replacing.
            builder.HasIndex(s => new { s.BatchId, s.ScheduledStartAtUtc })
                .IsUnique()
                .HasFilter("is_deleted = false AND batch_id IS NOT NULL AND status <> 'Cancelled'")
                .HasDatabaseName("ix_class_sessions_batch_id_scheduled_start_at_utc");
        }
    }
}
