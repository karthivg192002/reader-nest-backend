using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class RenameBatchDeletedCancellationReasons : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Classes cancelled by deleting/archiving a batch used to say just "Batch deleted." /
            // "Batch marked Archived." in the session list. The client asked for these to show
            // only who cancelled them, like every other cancellation ("Cancelled by {name} ({role})",
            // see CancelledBy.Describe). The batch row's updated_by is whoever deleted/archived it.
            migrationBuilder.Sql(@"
UPDATE class_sessions s
SET cancellation_reason = 'Cancelled by ' || CASE
        WHEN trim(coalesce(u.first_name, '') || ' ' || coalesce(u.last_name, '')) = '' THEN r.label
        ELSE trim(coalesce(u.first_name, '') || ' ' || coalesce(u.last_name, '')) || ' (' || r.label || ')'
    END
FROM batches b
JOIN users u ON u.id = b.updated_by
LEFT JOIN role_definitions rd ON rd.id = u.role_definition_id
CROSS JOIN LATERAL (SELECT CASE u.role
        WHEN 'AdmissionTeam' THEN 'Admission team'
        WHEN 'SubAdmin' THEN coalesce(rd.display_name, 'Staff')
        ELSE u.role
    END AS label) r
WHERE s.batch_id = b.id
  AND s.status = 'Cancelled'
  AND (s.cancellation_reason = 'Batch deleted.' OR s.cancellation_reason LIKE 'Batch marked %.');
");
            // Anything left (no record of who did it): drop the batch wording all the same.
            migrationBuilder.Sql(@"
UPDATE class_sessions
SET cancellation_reason = 'Cancelled by the academy'
WHERE status = 'Cancelled'
  AND (cancellation_reason = 'Batch deleted.' OR cancellation_reason LIKE 'Batch marked %.');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Data-only fix; the original wording isn't worth restoring.
        }
    }
}
