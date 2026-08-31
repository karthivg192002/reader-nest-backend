using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class AddBulkEmailHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "bulk_email_recipient_id",
                table: "notifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "bulk_email_blasts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sent_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    scope = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    total_recipients = table.Column<int>(type: "integer", nullable: false),
                    success_count = table.Column<int>(type: "integer", nullable: false),
                    failure_count = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bulk_email_blasts", x => x.id);
                    table.ForeignKey(
                        name: "fk_bulk_email_blasts_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bulk_email_blasts_users_sent_by_user_id",
                        column: x => x.sent_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bulk_email_recipients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bulk_email_blast_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    error_message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bulk_email_recipients", x => x.id);
                    table.ForeignKey(
                        name: "fk_bulk_email_recipients_bulk_email_blasts_bulk_email_blast_id",
                        column: x => x.bulk_email_blast_id,
                        principalTable: "bulk_email_blasts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bulk_email_recipients_users_recipient_user_id",
                        column: x => x.recipient_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "bulk_email_replies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bulk_email_recipient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    replied_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bulk_email_replies", x => x.id);
                    table.ForeignKey(
                        name: "fk_bulk_email_replies_bulk_email_recipients_bulk_email_recipie",
                        column: x => x.bulk_email_recipient_id,
                        principalTable: "bulk_email_recipients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_bulk_email_replies_users_parent_user_id",
                        column: x => x.parent_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_bulk_email_recipient_id",
                table: "notifications",
                column: "bulk_email_recipient_id");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_email_blasts_batch_id",
                table: "bulk_email_blasts",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_email_blasts_sent_by_user_id",
                table: "bulk_email_blasts",
                column: "sent_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_email_recipients_bulk_email_blast_id",
                table: "bulk_email_recipients",
                column: "bulk_email_blast_id");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_email_recipients_recipient_user_id",
                table: "bulk_email_recipients",
                column: "recipient_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_email_replies_bulk_email_recipient_id",
                table: "bulk_email_replies",
                column: "bulk_email_recipient_id",
                unique: true,
                filter: "\"is_deleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "ix_bulk_email_replies_parent_user_id",
                table: "bulk_email_replies",
                column: "parent_user_id");

            migrationBuilder.AddForeignKey(
                name: "fk_notifications_bulk_email_recipients_bulk_email_recipient_id",
                table: "notifications",
                column: "bulk_email_recipient_id",
                principalTable: "bulk_email_recipients",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_notifications_bulk_email_recipients_bulk_email_recipient_id",
                table: "notifications");

            migrationBuilder.DropTable(
                name: "bulk_email_replies");

            migrationBuilder.DropTable(
                name: "bulk_email_recipients");

            migrationBuilder.DropTable(
                name: "bulk_email_blasts");

            migrationBuilder.DropIndex(
                name: "ix_notifications_bulk_email_recipient_id",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "bulk_email_recipient_id",
                table: "notifications");
        }
    }
}
