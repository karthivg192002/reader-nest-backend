using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class AddMissingQueryIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_status_paid_at_utc",
                table: "payment_transactions",
                columns: new[] { "status", "paid_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_demo_bookings_parent_email",
                table: "demo_bookings",
                column: "parent_email");

            migrationBuilder.CreateIndex(
                name: "ix_class_sessions_meeting_room_id",
                table: "class_sessions",
                column: "meeting_room_id");

            migrationBuilder.CreateIndex(
                name: "ix_class_sessions_status_scheduled_start_at_utc",
                table: "class_sessions",
                columns: new[] { "status", "scheduled_start_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_class_sessions_teacher_profile_id_scheduled_start_at_utc",
                table: "class_sessions",
                columns: new[] { "teacher_profile_id", "scheduled_start_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payment_transactions_status_paid_at_utc",
                table: "payment_transactions");

            migrationBuilder.DropIndex(
                name: "ix_demo_bookings_parent_email",
                table: "demo_bookings");

            migrationBuilder.DropIndex(
                name: "ix_class_sessions_meeting_room_id",
                table: "class_sessions");

            migrationBuilder.DropIndex(
                name: "ix_class_sessions_status_scheduled_start_at_utc",
                table: "class_sessions");

            migrationBuilder.DropIndex(
                name: "ix_class_sessions_teacher_profile_id_scheduled_start_at_utc",
                table: "class_sessions");
        }
    }
}
