using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class AddUserRoleStatusAndEnrollmentFormStatusIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_users_role_status",
                table: "users",
                columns: new[] { "role", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_enrollment_forms_status",
                table: "enrollment_forms",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_users_role_status",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_enrollment_forms_status",
                table: "enrollment_forms");
        }
    }
}
