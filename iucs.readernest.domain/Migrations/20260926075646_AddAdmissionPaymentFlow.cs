using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class AddAdmissionPaymentFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "course_id",
                table: "demo_bookings",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_token",
                table: "demo_bookings",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "payment_verified_at_utc",
                table: "demo_bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "terms_accepted_at_utc",
                table: "demo_bookings",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_demo_bookings_course_id",
                table: "demo_bookings",
                column: "course_id");

            migrationBuilder.CreateIndex(
                name: "ix_demo_bookings_payment_token",
                table: "demo_bookings",
                column: "payment_token",
                unique: true,
                filter: "\"is_deleted\" = FALSE");

            migrationBuilder.AddForeignKey(
                name: "fk_demo_bookings_courses_course_id",
                table: "demo_bookings",
                column: "course_id",
                principalTable: "courses",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_demo_bookings_courses_course_id",
                table: "demo_bookings");

            migrationBuilder.DropIndex(
                name: "ix_demo_bookings_course_id",
                table: "demo_bookings");

            migrationBuilder.DropIndex(
                name: "ix_demo_bookings_payment_token",
                table: "demo_bookings");

            migrationBuilder.DropColumn(
                name: "course_id",
                table: "demo_bookings");

            migrationBuilder.DropColumn(
                name: "payment_token",
                table: "demo_bookings");

            migrationBuilder.DropColumn(
                name: "payment_verified_at_utc",
                table: "demo_bookings");

            migrationBuilder.DropColumn(
                name: "terms_accepted_at_utc",
                table: "demo_bookings");
        }
    }
}
