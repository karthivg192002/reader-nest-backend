using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class RenamePayoutRateToRatePerMinute : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Pure rename -- the stored numbers themselves keep whatever value each rate card
            // already had. Those values were configured as flat per-session amounts, so once
            // this deploys they'll price every session as (old value) * (session's own
            // scheduled minutes), which is not what any admin actually configured. There's no
            // reliable duration to divide by here (a rate card has no duration of its own, and
            // historically covered sessions of several different lengths), so every existing
            // rate card must be manually reconfigured to a real per-minute value after this
            // migration runs -- this is a deliberate follow-up action, not a gap in the
            // migration itself.
            migrationBuilder.RenameColumn(
                name: "rate_per_session",
                table: "payout_rates",
                newName: "rate_per_minute");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "rate_per_minute",
                table: "payout_rates",
                newName: "rate_per_session");
        }
    }
}
