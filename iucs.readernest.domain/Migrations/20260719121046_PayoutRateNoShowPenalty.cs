using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class PayoutRateNoShowPenalty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default 100: existing rates keep deducting the full session rate on a
            // teacher no-show, exactly as before this column existed.
            migrationBuilder.AddColumn<decimal>(
                name: "teacher_no_show_penalty_percent",
                table: "payout_rates",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: false,
                defaultValue: 100m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "teacher_no_show_penalty_percent",
                table: "payout_rates");
        }
    }
}
