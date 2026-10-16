using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class PaymentLinkExpiryAndDemoFollowUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "payment_link_code",
                table: "invoices",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "payment_link_expires_at_utc",
                table: "invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "follow_up_email_count",
                table: "demo_bookings",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_follow_up_email_at_utc",
                table: "demo_bookings",
                type: "timestamp with time zone",
                nullable: true);

            // Client (2026-10-16): payment links last 48 hours. Links already shared for open
            // invoices get 48 hours from this deploy rather than dying on the spot.
            migrationBuilder.Sql(@"
UPDATE invoices
SET payment_link_expires_at_utc = now() + interval '48 hours'
WHERE status IN ('Pending', 'PartiallyPaid', 'Overdue');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "payment_link_code",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "payment_link_expires_at_utc",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "follow_up_email_count",
                table: "demo_bookings");

            migrationBuilder.DropColumn(
                name: "last_follow_up_email_at_utc",
                table: "demo_bookings");
        }
    }
}
