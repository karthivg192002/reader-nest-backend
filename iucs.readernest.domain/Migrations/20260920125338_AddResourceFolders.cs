using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "folder_id",
                table: "resources",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "resource_folders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    parent_folder_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resource_folders", x => x.id);
                    table.ForeignKey(
                        name: "fk_resource_folders_resource_folders_parent_folder_id",
                        column: x => x.parent_folder_id,
                        principalTable: "resource_folders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "resource_folder_accesses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    folder_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resource_folder_accesses", x => x.id);
                    table.ForeignKey(
                        name: "fk_resource_folder_accesses_parent_profiles_parent_profile_id",
                        column: x => x.parent_profile_id,
                        principalTable: "parent_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_resource_folder_accesses_resource_folders_folder_id",
                        column: x => x.folder_id,
                        principalTable: "resource_folders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_resources_folder_id",
                table: "resources",
                column: "folder_id");

            migrationBuilder.CreateIndex(
                name: "ix_resource_folder_accesses_folder_id_parent_profile_id",
                table: "resource_folder_accesses",
                columns: new[] { "folder_id", "parent_profile_id" },
                unique: true,
                filter: "\"is_deleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "ix_resource_folder_accesses_parent_profile_id",
                table: "resource_folder_accesses",
                column: "parent_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_resource_folders_parent_folder_id",
                table: "resource_folders",
                column: "parent_folder_id");

            migrationBuilder.AddForeignKey(
                name: "fk_resources_resource_folders_folder_id",
                table: "resources",
                column: "folder_id",
                principalTable: "resource_folders",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_resources_resource_folders_folder_id",
                table: "resources");

            migrationBuilder.DropTable(
                name: "resource_folder_accesses");

            migrationBuilder.DropTable(
                name: "resource_folders");

            migrationBuilder.DropIndex(
                name: "ix_resources_folder_id",
                table: "resources");

            migrationBuilder.DropColumn(
                name: "folder_id",
                table: "resources");
        }
    }
}
