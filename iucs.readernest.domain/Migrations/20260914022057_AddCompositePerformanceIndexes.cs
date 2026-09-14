using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class AddCompositePerformanceIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_class_sessions_batch_id",
                table: "class_sessions");

            migrationBuilder.DropIndex(
                name: "ix_class_sessions_teacher_profile_id",
                table: "class_sessions");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient_user_id_created_at_utc",
                table: "notifications",
                columns: new[] { "recipient_user_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_class_sessions_batch_id_scheduled_start_at_utc",
                table: "class_sessions",
                columns: new[] { "batch_id", "scheduled_start_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_class_sessions_teacher_profile_id_status",
                table: "class_sessions",
                columns: new[] { "teacher_profile_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_chat_escalations_status_created_at_utc",
                table: "chat_escalations",
                columns: new[] { "status", "created_at_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notifications_recipient_user_id_created_at_utc",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_class_sessions_batch_id_scheduled_start_at_utc",
                table: "class_sessions");

            migrationBuilder.DropIndex(
                name: "ix_class_sessions_teacher_profile_id_status",
                table: "class_sessions");

            migrationBuilder.DropIndex(
                name: "ix_chat_escalations_status_created_at_utc",
                table: "chat_escalations");

            migrationBuilder.CreateIndex(
                name: "ix_class_sessions_batch_id",
                table: "class_sessions",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_class_sessions_teacher_profile_id",
                table: "class_sessions",
                column: "teacher_profile_id");
        }
    }
}
