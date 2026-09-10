using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class AddClassSessionRecordingMissingAlert : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "recording_missing_alert_sent_at_utc",
                table: "class_sessions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "recording_missing_alert_sent_at_utc",
                table: "class_sessions");
        }
    }
}
