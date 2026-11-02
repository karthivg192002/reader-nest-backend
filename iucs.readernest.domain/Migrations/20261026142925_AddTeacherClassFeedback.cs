using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class AddTeacherClassFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "teacher_admin_note",
                table: "class_sessions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "teacher_class_status",
                table: "class_sessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "teacher_feedback_at_utc",
                table: "class_sessions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "teacher_admin_note",
                table: "class_sessions");

            migrationBuilder.DropColumn(
                name: "teacher_class_status",
                table: "class_sessions");

            migrationBuilder.DropColumn(
                name: "teacher_feedback_at_utc",
                table: "class_sessions");
        }
    }
}
