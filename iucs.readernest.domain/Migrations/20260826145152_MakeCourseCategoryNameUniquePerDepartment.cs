using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class MakeCourseCategoryNameUniquePerDepartment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_course_categories_department_id",
                table: "course_categories");

            migrationBuilder.DropIndex(
                name: "ix_course_categories_name",
                table: "course_categories");

            migrationBuilder.CreateIndex(
                name: "ix_course_categories_department_id_name",
                table: "course_categories",
                columns: new[] { "department_id", "name" },
                unique: true,
                filter: "\"is_deleted\" = FALSE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_course_categories_department_id_name",
                table: "course_categories");

            migrationBuilder.CreateIndex(
                name: "ix_course_categories_department_id",
                table: "course_categories",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_course_categories_name",
                table: "course_categories",
                column: "name",
                unique: true,
                filter: "\"is_deleted\" = FALSE");
        }
    }
}
