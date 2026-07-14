using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class ParentPaymentAccountMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "payment_account_id",
                table: "parent_profiles",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_parent_profiles_payment_account_id",
                table: "parent_profiles",
                column: "payment_account_id");

            migrationBuilder.AddForeignKey(
                name: "fk_parent_profiles_payment_accounts_payment_account_id",
                table: "parent_profiles",
                column: "payment_account_id",
                principalTable: "payment_accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_parent_profiles_payment_accounts_payment_account_id",
                table: "parent_profiles");

            migrationBuilder.DropIndex(
                name: "ix_parent_profiles_payment_account_id",
                table: "parent_profiles");

            migrationBuilder.DropColumn(
                name: "payment_account_id",
                table: "parent_profiles");
        }
    }
}
