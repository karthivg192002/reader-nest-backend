using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class AddStaffPayrollModule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "teacher_payout_per_class",
                table: "batches",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "staff_compensation_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    fixed_monthly_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    collection_percentage = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_compensation_settings", x => x.id);
                    table.ForeignKey(
                        name: "fk_staff_compensation_settings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "staff_payouts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_year = table.Column<int>(type: "integer", nullable: false),
                    period_month = table.Column<int>(type: "integer", nullable: false),
                    basis = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    collection_amount_raw = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    collection_amount_rounded = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    applied_percentage = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    applied_fixed_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    paid_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_payouts", x => x.id);
                    table.ForeignKey(
                        name: "fk_staff_payouts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_staff_compensation_settings_user_id_effective_from",
                table: "staff_compensation_settings",
                columns: new[] { "user_id", "effective_from" },
                unique: true,
                filter: "\"is_deleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "ix_staff_payouts_user_id_period_year_period_month",
                table: "staff_payouts",
                columns: new[] { "user_id", "period_year", "period_month" },
                unique: true,
                filter: "\"is_deleted\" = FALSE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "staff_compensation_settings");

            migrationBuilder.DropTable(
                name: "staff_payouts");

            migrationBuilder.DropColumn(
                name: "teacher_payout_per_class",
                table: "batches");
        }
    }
}
