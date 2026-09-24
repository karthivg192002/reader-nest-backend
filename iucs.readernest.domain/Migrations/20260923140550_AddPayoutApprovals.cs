using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class AddPayoutApprovals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "amount_before_review",
                table: "payout_items",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "delivered_minutes",
                table: "payout_items",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "review_decision",
                table: "payout_items",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "reviewed_at_utc",
                table: "payout_items",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "reviewed_by_user_id",
                table: "payout_items",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "scheduled_minutes",
                table: "payout_items",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "amount_before_review",
                table: "payout_items");

            migrationBuilder.DropColumn(
                name: "delivered_minutes",
                table: "payout_items");

            migrationBuilder.DropColumn(
                name: "review_decision",
                table: "payout_items");

            migrationBuilder.DropColumn(
                name: "reviewed_at_utc",
                table: "payout_items");

            migrationBuilder.DropColumn(
                name: "reviewed_by_user_id",
                table: "payout_items");

            migrationBuilder.DropColumn(
                name: "scheduled_minutes",
                table: "payout_items");
        }
    }
}
