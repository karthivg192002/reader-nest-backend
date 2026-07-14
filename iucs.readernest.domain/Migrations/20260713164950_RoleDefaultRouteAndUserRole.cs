using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class RoleDefaultRouteAndUserRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "role_definition_id",
                table: "users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "default_route",
                table: "role_definitions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_role_definition_id",
                table: "users",
                column: "role_definition_id");

            migrationBuilder.AddForeignKey(
                name: "fk_users_role_definitions_role_definition_id",
                table: "users",
                column: "role_definition_id",
                principalTable: "role_definitions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_users_role_definitions_role_definition_id",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_role_definition_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "role_definition_id",
                table: "users");

            migrationBuilder.DropColumn(
                name: "default_route",
                table: "role_definitions");
        }
    }
}
