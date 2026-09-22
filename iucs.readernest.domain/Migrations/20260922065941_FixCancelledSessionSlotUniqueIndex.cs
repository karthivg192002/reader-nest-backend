using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class FixCancelledSessionSlotUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_class_sessions_batch_id_scheduled_start_at_utc",
                table: "class_sessions");

            migrationBuilder.CreateIndex(
                name: "ix_class_sessions_batch_id_scheduled_start_at_utc",
                table: "class_sessions",
                columns: new[] { "batch_id", "scheduled_start_at_utc" },
                unique: true,
                filter: "is_deleted = false AND batch_id IS NOT NULL AND status <> 'Cancelled'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_class_sessions_batch_id_scheduled_start_at_utc",
                table: "class_sessions");

            migrationBuilder.CreateIndex(
                name: "ix_class_sessions_batch_id_scheduled_start_at_utc",
                table: "class_sessions",
                columns: new[] { "batch_id", "scheduled_start_at_utc" },
                unique: true,
                filter: "is_deleted = false AND batch_id IS NOT NULL");
        }
    }
}
