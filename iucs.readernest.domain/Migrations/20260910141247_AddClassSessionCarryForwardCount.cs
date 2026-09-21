using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class AddClassSessionCarryForwardCount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "carry_forward_count",
                table: "class_sessions",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "carry_forward_count",
                table: "class_sessions");
        }
    }
}
