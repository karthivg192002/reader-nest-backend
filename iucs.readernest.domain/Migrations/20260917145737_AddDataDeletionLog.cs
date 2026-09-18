using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class AddDataDeletionLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_deletion_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    deletion_batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    root_entity_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    root_entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    table_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_json = table.Column<string>(type: "text", nullable: false),
                    deleted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_deletion_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_data_deletion_logs_deleted_by_user_id",
                table: "data_deletion_logs",
                column: "deleted_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_deletion_logs_deletion_batch_id",
                table: "data_deletion_logs",
                column: "deletion_batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_data_deletion_logs_root_entity_type_root_entity_id",
                table: "data_deletion_logs",
                columns: new[] { "root_entity_type", "root_entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_data_deletion_logs_table_name_record_id",
                table: "data_deletion_logs",
                columns: new[] { "table_name", "record_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_deletion_logs");
        }
    }
}
