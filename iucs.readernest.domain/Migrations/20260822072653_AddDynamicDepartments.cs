using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class AddDynamicDepartments : Migration
    {
        // Matches WellKnownDepartments in iucs.readernest.domain.Entities.Academics -- kept as a
        // literal string here rather than referencing that type, since migrations must stay
        // buildable against whatever the entity model looked like at ANY point in history, not
        // just today's.
        private const string PhonicsId = "00000000-0000-0000-0000-000000000001";
        private const string MathsId = "00000000-0000-0000-0000-000000000002";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) The new departments table, seeded under fixed ids first -- every backfill step
            // below needs these rows to already exist before any department_id FK can point at them.
            migrationBuilder.CreateTable(
                name: "departments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_departments", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_departments_name",
                table: "departments",
                column: "name",
                unique: true,
                filter: "\"is_deleted\" = FALSE");

            migrationBuilder.InsertData(
                table: "departments",
                columns: new[] { "id", "name", "description", "is_active", "created_at_utc", "is_deleted" },
                values: new object[,]
                {
                    { new Guid(PhonicsId), "Phonics", null, true, DateTime.UtcNow, false },
                    { new Guid(MathsId), "Maths", null, true, DateTime.UtcNow, false },
                });

            // 2) Add the new FK columns nullable first, on every table that had the old string
            // enum column -- can't add them NOT NULL yet because real existing rows have no
            // value for a column that doesn't exist until backfilled below.
            migrationBuilder.AddColumn<Guid>(name: "department_id", table: "teacher_profiles", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "department_id", table: "payment_accounts", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "department_id", table: "invoices", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "department_id", table: "demo_bookings", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "department_id", table: "courses", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<Guid>(name: "department_id", table: "course_categories", type: "uuid", nullable: true);

            // 3) Backfill every existing row from its old string value. Any row whose old
            // "department" text isn't literally 'Phonics' or 'Maths' (there shouldn't be any --
            // the enum only ever had those two members -- but a raw DB edit or a bad migration
            // upstream is not impossible) falls back to Phonics rather than staying NULL, so the
            // NOT NULL columns below don't fail to apply.
            migrationBuilder.Sql($@"
                UPDATE teacher_profiles SET department_id =
                    CASE WHEN department = 'Maths' THEN '{MathsId}'::uuid
                         WHEN department = 'Phonics' THEN '{PhonicsId}'::uuid
                         ELSE NULL END
                WHERE department IS NOT NULL;

                UPDATE payment_accounts SET department_id =
                    CASE WHEN department = 'Maths' THEN '{MathsId}'::uuid ELSE '{PhonicsId}'::uuid END;

                UPDATE invoices SET department_id =
                    CASE WHEN department = 'Maths' THEN '{MathsId}'::uuid ELSE '{PhonicsId}'::uuid END;

                UPDATE demo_bookings SET department_id =
                    CASE WHEN department = 'Maths' THEN '{MathsId}'::uuid
                         WHEN department = 'Phonics' THEN '{PhonicsId}'::uuid
                         ELSE NULL END
                WHERE department IS NOT NULL;

                UPDATE courses SET department_id =
                    CASE WHEN department = 'Maths' THEN '{MathsId}'::uuid ELSE '{PhonicsId}'::uuid END;

                UPDATE course_categories SET department_id =
                    CASE WHEN department = 'Maths' THEN '{MathsId}'::uuid ELSE '{PhonicsId}'::uuid END;
            ");

            // 4) Now that every row has a real value, tighten the columns that are non-nullable
            // on the entity (courses, course_categories, invoices, payment_accounts);
            // teacher_profiles/demo_bookings stay nullable, matching Department? on those entities.
            migrationBuilder.AlterColumn<Guid>(name: "department_id", table: "payment_accounts", type: "uuid", nullable: false, oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
            migrationBuilder.AlterColumn<Guid>(name: "department_id", table: "invoices", type: "uuid", nullable: false, oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
            migrationBuilder.AlterColumn<Guid>(name: "department_id", table: "courses", type: "uuid", nullable: false, oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
            migrationBuilder.AlterColumn<Guid>(name: "department_id", table: "course_categories", type: "uuid", nullable: false, oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);

            // 5) Drop the old enum-string columns and their index, now that department_id fully
            // replaces them.
            migrationBuilder.DropIndex(name: "ix_payment_accounts_department", table: "payment_accounts");
            migrationBuilder.DropColumn(name: "department", table: "teacher_profiles");
            migrationBuilder.DropColumn(name: "department", table: "payment_accounts");
            migrationBuilder.DropColumn(name: "department", table: "invoices");
            migrationBuilder.DropColumn(name: "department", table: "demo_bookings");
            migrationBuilder.DropColumn(name: "department", table: "courses");
            migrationBuilder.DropColumn(name: "department", table: "course_categories");

            // 6) Indexes + FKs on the new column, now that it's fully populated and typed.
            migrationBuilder.CreateIndex(name: "ix_teacher_profiles_department_id", table: "teacher_profiles", column: "department_id");
            migrationBuilder.CreateIndex(
                name: "ix_payment_accounts_department_id",
                table: "payment_accounts",
                column: "department_id",
                unique: true,
                filter: "\"is_deleted\" = FALSE");
            migrationBuilder.CreateIndex(name: "ix_invoices_department_id", table: "invoices", column: "department_id");
            migrationBuilder.CreateIndex(name: "ix_demo_bookings_department_id", table: "demo_bookings", column: "department_id");
            migrationBuilder.CreateIndex(name: "ix_courses_department_id", table: "courses", column: "department_id");
            migrationBuilder.CreateIndex(name: "ix_course_categories_department_id", table: "course_categories", column: "department_id");

            migrationBuilder.AddForeignKey(
                name: "fk_course_categories_departments_department_id", table: "course_categories", column: "department_id",
                principalTable: "departments", principalColumn: "id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_courses_departments_department_id", table: "courses", column: "department_id",
                principalTable: "departments", principalColumn: "id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_demo_bookings_departments_department_id", table: "demo_bookings", column: "department_id",
                principalTable: "departments", principalColumn: "id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_invoices_departments_department_id", table: "invoices", column: "department_id",
                principalTable: "departments", principalColumn: "id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_payment_accounts_departments_department_id", table: "payment_accounts", column: "department_id",
                principalTable: "departments", principalColumn: "id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(
                name: "fk_teacher_profiles_departments_department_id", table: "teacher_profiles", column: "department_id",
                principalTable: "departments", principalColumn: "id", onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "fk_course_categories_departments_department_id", table: "course_categories");
            migrationBuilder.DropForeignKey(name: "fk_courses_departments_department_id", table: "courses");
            migrationBuilder.DropForeignKey(name: "fk_demo_bookings_departments_department_id", table: "demo_bookings");
            migrationBuilder.DropForeignKey(name: "fk_invoices_departments_department_id", table: "invoices");
            migrationBuilder.DropForeignKey(name: "fk_payment_accounts_departments_department_id", table: "payment_accounts");
            migrationBuilder.DropForeignKey(name: "fk_teacher_profiles_departments_department_id", table: "teacher_profiles");

            migrationBuilder.DropIndex(name: "ix_teacher_profiles_department_id", table: "teacher_profiles");
            migrationBuilder.DropIndex(name: "ix_payment_accounts_department_id", table: "payment_accounts");
            migrationBuilder.DropIndex(name: "ix_invoices_department_id", table: "invoices");
            migrationBuilder.DropIndex(name: "ix_demo_bookings_department_id", table: "demo_bookings");
            migrationBuilder.DropIndex(name: "ix_courses_department_id", table: "courses");
            migrationBuilder.DropIndex(name: "ix_course_categories_department_id", table: "course_categories");

            // Restore the old string columns nullable first, backfill from department_id
            // (any admin-added department beyond Phonics/Maths has no enum equivalent to roll
            // back to -- those rows fall back to Phonics, same convention as the forward migration
            // uses for unrecognized values), then tighten to NOT NULL where the entity had it.
            migrationBuilder.AddColumn<string>(name: "department", table: "teacher_profiles", type: "character varying(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<string>(name: "department", table: "payment_accounts", type: "character varying(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<string>(name: "department", table: "invoices", type: "character varying(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<string>(name: "department", table: "demo_bookings", type: "character varying(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<string>(name: "department", table: "courses", type: "character varying(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<string>(name: "department", table: "course_categories", type: "character varying(64)", maxLength: 64, nullable: true);

            migrationBuilder.Sql($@"
                UPDATE teacher_profiles SET department = CASE WHEN department_id = '{MathsId}'::uuid THEN 'Maths' WHEN department_id IS NOT NULL THEN 'Phonics' ELSE NULL END;
                UPDATE payment_accounts SET department = CASE WHEN department_id = '{MathsId}'::uuid THEN 'Maths' ELSE 'Phonics' END;
                UPDATE invoices SET department = CASE WHEN department_id = '{MathsId}'::uuid THEN 'Maths' ELSE 'Phonics' END;
                UPDATE demo_bookings SET department = CASE WHEN department_id = '{MathsId}'::uuid THEN 'Maths' WHEN department_id IS NOT NULL THEN 'Phonics' ELSE NULL END;
                UPDATE courses SET department = CASE WHEN department_id = '{MathsId}'::uuid THEN 'Maths' ELSE 'Phonics' END;
                UPDATE course_categories SET department = CASE WHEN department_id = '{MathsId}'::uuid THEN 'Maths' ELSE 'Phonics' END;
            ");

            migrationBuilder.AlterColumn<string>(name: "department", table: "payment_accounts", type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "", oldClrType: typeof(string), oldType: "character varying(64)", oldMaxLength: 64, oldNullable: true);
            migrationBuilder.AlterColumn<string>(name: "department", table: "invoices", type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "", oldClrType: typeof(string), oldType: "character varying(64)", oldMaxLength: 64, oldNullable: true);
            migrationBuilder.AlterColumn<string>(name: "department", table: "courses", type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "", oldClrType: typeof(string), oldType: "character varying(64)", oldMaxLength: 64, oldNullable: true);
            migrationBuilder.AlterColumn<string>(name: "department", table: "course_categories", type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "", oldClrType: typeof(string), oldType: "character varying(64)", oldMaxLength: 64, oldNullable: true);

            migrationBuilder.DropColumn(name: "department_id", table: "teacher_profiles");
            migrationBuilder.DropColumn(name: "department_id", table: "payment_accounts");
            migrationBuilder.DropColumn(name: "department_id", table: "invoices");
            migrationBuilder.DropColumn(name: "department_id", table: "demo_bookings");
            migrationBuilder.DropColumn(name: "department_id", table: "courses");
            migrationBuilder.DropColumn(name: "department_id", table: "course_categories");

            migrationBuilder.DropTable(name: "departments");

            migrationBuilder.CreateIndex(
                name: "ix_payment_accounts_department",
                table: "payment_accounts",
                column: "department",
                unique: true,
                filter: "\"is_deleted\" = FALSE");
        }
    }
}
