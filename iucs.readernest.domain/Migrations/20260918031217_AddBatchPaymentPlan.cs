using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchPaymentPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "payment_after_sessions_count",
                table: "batches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "payment_due_date",
                table: "batches",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_plan_type",
                table: "batches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "FullPaymentDone");

            migrationBuilder.AddColumn<DateTime>(
                name: "payment_reminder_sent_at_utc",
                table: "batches",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payment_after_sessions_count",
                table: "batches");

            migrationBuilder.DropColumn(
                name: "payment_due_date",
                table: "batches");

            migrationBuilder.DropColumn(
                name: "payment_plan_type",
                table: "batches");

            migrationBuilder.DropColumn(
                name: "payment_reminder_sent_at_utc",
                table: "batches");
        }
    }
}
