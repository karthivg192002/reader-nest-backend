using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class AddFeeSuspensionChildId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "child_id",
                table: "fee_suspensions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_fee_suspensions_child_id",
                table: "fee_suspensions",
                column: "child_id");

            migrationBuilder.AddForeignKey(
                name: "fk_fee_suspensions_children_child_id",
                table: "fee_suspensions",
                column: "child_id",
                principalTable: "children",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_fee_suspensions_children_child_id",
                table: "fee_suspensions");

            migrationBuilder.DropIndex(
                name: "ix_fee_suspensions_child_id",
                table: "fee_suspensions");

            migrationBuilder.DropColumn(
                name: "child_id",
                table: "fee_suspensions");
        }
    }
}
