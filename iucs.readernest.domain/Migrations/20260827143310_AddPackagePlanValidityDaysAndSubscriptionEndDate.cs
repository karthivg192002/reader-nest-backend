using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class AddPackagePlanValidityDaysAndSubscriptionEndDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "end_date",
                table: "subscriptions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "validity_days",
                table: "package_plans",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_status_end_date",
                table: "subscriptions",
                columns: new[] { "status", "end_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_subscriptions_status_end_date",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "end_date",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "validity_days",
                table: "package_plans");
        }
    }
}
