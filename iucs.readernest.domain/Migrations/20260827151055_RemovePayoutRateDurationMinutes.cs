using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class RemovePayoutRateDurationMinutes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payout_rates_teacher_profile_id_duration_minutes_effective_",
                table: "payout_rates");

            // Existing data almost certainly has several rows per (teacher_profile_id,
            // effective_from) -- one per old duration tier (20/30/45/60-min), each often at a
            // different rate. Dropping duration_minutes alone would leave duplicates the new
            // unique index below can't accept. Deliberately keeps the HIGHEST RatePerSession
            // among duplicates as the new flat rate for that teacher/date, so this migration
            // never silently pays anyone less than at least one of their prior duration tiers
            // already did. Soft-deletes (not hard-deletes) the rest, consistent with how every
            // other delete in this app already works, and leaves a full audit trail of exactly
            // which rows this migration removed.
            migrationBuilder.Sql(@"
                UPDATE payout_rates
                SET is_deleted = TRUE, deleted_at_utc = NOW()
                WHERE is_deleted = FALSE
                  AND id NOT IN (
                    SELECT DISTINCT ON (teacher_profile_id, effective_from) id
                    FROM payout_rates
                    WHERE is_deleted = FALSE
                    ORDER BY teacher_profile_id, effective_from, rate_per_session DESC, id
                  );
            ");

            migrationBuilder.DropColumn(
                name: "duration_minutes",
                table: "payout_rates");

            migrationBuilder.CreateIndex(
                name: "ix_payout_rates_teacher_profile_id_effective_from",
                table: "payout_rates",
                columns: new[] { "teacher_profile_id", "effective_from" },
                unique: true,
                filter: "\"is_deleted\" = FALSE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payout_rates_teacher_profile_id_effective_from",
                table: "payout_rates");

            migrationBuilder.AddColumn<int>(
                name: "duration_minutes",
                table: "payout_rates",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_payout_rates_teacher_profile_id_duration_minutes_effective_",
                table: "payout_rates",
                columns: new[] { "teacher_profile_id", "duration_minutes", "effective_from" },
                unique: true,
                filter: "\"is_deleted\" = FALSE");
        }
    }
}
