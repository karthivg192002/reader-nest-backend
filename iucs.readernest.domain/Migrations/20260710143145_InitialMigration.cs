using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace iucs.readernest.domain.Migrations
{
    /// <inheritdoc />
    public partial class InitialMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    changes_json = table.Column<string>(type: "text", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "course_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    department = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_course_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "holidays",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_holidays", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    department = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    gateway_provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    gateway_account_ref = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
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
                    table.PrimaryKey("pk_payment_accounts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    last_login_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "courses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    total_sessions = table.Column<int>(type: "integer", nullable: false),
                    department = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
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
                    table.PrimaryKey("pk_courses", x => x.id);
                    table.ForeignKey(
                        name: "fk_courses_course_categories_course_category_id",
                        column: x => x.course_category_id,
                        principalTable: "course_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    channel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    subject = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    body = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    read_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    metadata_json = table.Column<string>(type: "text", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_notifications_users_recipient_user_id",
                        column: x => x.recipient_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parent_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    enrollment_form_completed = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parent_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_parent_profiles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sub_admin_permissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    module = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    can_view = table.Column<bool>(type: "boolean", nullable: false),
                    can_create = table.Column<bool>(type: "boolean", nullable: false),
                    can_edit = table.Column<bool>(type: "boolean", nullable: false),
                    can_delete = table.Column<bool>(type: "boolean", nullable: false),
                    can_approve = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sub_admin_permissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_sub_admin_permissions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "teacher_profiles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    bio = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    specialization = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    department = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teacher_profiles", x => x.id);
                    table.ForeignKey(
                        name: "fk_teacher_profiles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "package_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: true),
                    billing_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    billing_cycle = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    sessions_included = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("pk_package_plans", x => x.id);
                    table.ForeignKey(
                        name: "fk_package_plans_courses_course_id",
                        column: x => x.course_id,
                        principalTable: "courses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "children",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    first_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    last_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    date_of_birth = table.Column<DateOnly>(type: "date", nullable: true),
                    gender = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    academic_level = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
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
                    table.PrimaryKey("pk_children", x => x.id);
                    table.ForeignKey(
                        name: "fk_children_parent_profiles_parent_profile_id",
                        column: x => x.parent_profile_id,
                        principalTable: "parent_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "batches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    capacity = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batches", x => x.id);
                    table.ForeignKey(
                        name: "fk_batches_courses_course_id",
                        column: x => x.course_id,
                        principalTable: "courses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_batches_teacher_profiles_teacher_profile_id",
                        column: x => x.teacher_profile_id,
                        principalTable: "teacher_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "leave_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    start_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    review_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_leave_requests", x => x.id);
                    table.ForeignKey(
                        name: "fk_leave_requests_teacher_profiles_teacher_profile_id",
                        column: x => x.teacher_profile_id,
                        principalTable: "teacher_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payout_rates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    rate_per_session = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    effective_from = table.Column<DateOnly>(type: "date", nullable: false),
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
                    table.PrimaryKey("pk_payout_rates", x => x.id);
                    table.ForeignKey(
                        name: "fk_payout_rates_teacher_profiles_teacher_profile_id",
                        column: x => x.teacher_profile_id,
                        principalTable: "teacher_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payouts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_year = table.Column<int>(type: "integer", nullable: false),
                    period_month = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    finalized_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    email_sent_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payouts", x => x.id);
                    table.ForeignKey(
                        name: "fk_payouts_teacher_profiles_teacher_profile_id",
                        column: x => x.teacher_profile_id,
                        principalTable: "teacher_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "enrollment_forms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    child_id = table.Column<Guid>(type: "uuid", nullable: true),
                    form_data_json = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    submitted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_enrollment_forms", x => x.id);
                    table.ForeignKey(
                        name: "fk_enrollment_forms_children_child_id",
                        column: x => x.child_id,
                        principalTable: "children",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_enrollment_forms_parent_profiles_parent_profile_id",
                        column: x => x.parent_profile_id,
                        principalTable: "parent_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    child_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    next_billing_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    cancelled_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscriptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_subscriptions_children_child_id",
                        column: x => x.child_id,
                        principalTable: "children",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subscriptions_package_plans_package_plan_id",
                        column: x => x.package_plan_id,
                        principalTable: "package_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_subscriptions_parent_profiles_parent_profile_id",
                        column: x => x.parent_profile_id,
                        principalTable: "parent_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "batch_enrollments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    child_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_batch_enrollments", x => x.id);
                    table.ForeignKey(
                        name: "fk_batch_enrollments_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_batch_enrollments_children_child_id",
                        column: x => x.child_id,
                        principalTable: "children",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "class_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    teacher_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scheduled_start_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    scheduled_end_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    actual_start_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    actual_end_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    meeting_room_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    rescheduled_from_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    carried_forward_from_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    summary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_class_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_class_sessions_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_class_sessions_class_sessions_carried_forward_from_session_",
                        column: x => x.carried_forward_from_session_id,
                        principalTable: "class_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_class_sessions_class_sessions_rescheduled_from_session_id",
                        column: x => x.rescheduled_from_session_id,
                        principalTable: "class_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_class_sessions_teacher_profiles_teacher_profile_id",
                        column: x => x.teacher_profile_id,
                        principalTable: "teacher_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "resources",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    file_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    mime_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    course_id = table.Column<Guid>(type: "uuid", nullable: true),
                    batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_downloadable = table.Column<bool>(type: "boolean", nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resources", x => x.id);
                    table.ForeignKey(
                        name: "fk_resources_batches_batch_id",
                        column: x => x.batch_id,
                        principalTable: "batches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_resources_courses_course_id",
                        column: x => x.course_id,
                        principalTable: "courses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    parent_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    child_id = table.Column<Guid>(type: "uuid", nullable: true),
                    subscription_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payment_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    department = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    amount_paid = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    issued_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("pk_invoices", x => x.id);
                    table.ForeignKey(
                        name: "fk_invoices_children_child_id",
                        column: x => x.child_id,
                        principalTable: "children",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_parent_profiles_parent_profile_id",
                        column: x => x.parent_profile_id,
                        principalTable: "parent_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_payment_accounts_payment_account_id",
                        column: x => x.payment_account_id,
                        principalTable: "payment_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_subscriptions_subscription_id",
                        column: x => x.subscription_id,
                        principalTable: "subscriptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payout_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payout_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payout_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_payout_items_class_sessions_class_session_id",
                        column: x => x.class_session_id,
                        principalTable: "class_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payout_items_payouts_payout_id",
                        column: x => x.payout_id,
                        principalTable: "payouts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "session_attendances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    participant_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    child_id = table.Column<Guid>(type: "uuid", nullable: true),
                    teacher_profile_id = table.Column<Guid>(type: "uuid", nullable: true),
                    joined_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    left_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_attendances", x => x.id);
                    table.ForeignKey(
                        name: "fk_session_attendances_children_child_id",
                        column: x => x.child_id,
                        principalTable: "children",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_session_attendances_class_sessions_class_session_id",
                        column: x => x.class_session_id,
                        principalTable: "class_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_session_attendances_teacher_profiles_teacher_profile_id",
                        column: x => x.teacher_profile_id,
                        principalTable: "teacher_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "session_recordings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    duration_seconds = table.Column<int>(type: "integer", nullable: true),
                    expires_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_session_recordings", x => x.id);
                    table.ForeignKey(
                        name: "fk_session_recordings_class_sessions_class_session_id",
                        column: x => x.class_session_id,
                        principalTable: "class_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "resource_accesses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    visible_on_dashboard = table.Column<bool>(type: "boolean", nullable: false),
                    granted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resource_accesses", x => x.id);
                    table.ForeignKey(
                        name: "fk_resource_accesses_parent_profiles_parent_profile_id",
                        column: x => x.parent_profile_id,
                        principalTable: "parent_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_resource_accesses_resources_resource_id",
                        column: x => x.resource_id,
                        principalTable: "resources",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "demo_bookings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    parent_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    parent_phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    child_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    child_age = table.Column<int>(type: "integer", nullable: true),
                    department = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    conversion_status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    follow_up_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    payment_link_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_demo_bookings", x => x.id);
                    table.ForeignKey(
                        name: "fk_demo_bookings_class_sessions_class_session_id",
                        column: x => x.class_session_id,
                        principalTable: "class_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_demo_bookings_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fee_suspensions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    suspended_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    lifted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    auto_restored = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fee_suspensions", x => x.id);
                    table.ForeignKey(
                        name: "fk_fee_suspensions_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fee_suspensions_parent_profiles_parent_profile_id",
                        column: x => x.parent_profile_id,
                        principalTable: "parent_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    gateway_transaction_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    method = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    paid_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    receipt_number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    receipt_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_transactions_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_transactions_payment_accounts_payment_account_id",
                        column: x => x.payment_account_id,
                        principalTable: "payment_accounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "demo_feedbacks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    demo_booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_profile_id = table.Column<Guid>(type: "uuid", nullable: false),
                    academic_level = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    strengths = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    improvement_areas = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    recommended_course_id = table.Column<Guid>(type: "uuid", nullable: true),
                    suggested_batch_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    remarks = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    submitted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_demo_feedbacks", x => x.id);
                    table.ForeignKey(
                        name: "fk_demo_feedbacks_courses_recommended_course_id",
                        column: x => x.recommended_course_id,
                        principalTable: "courses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_demo_feedbacks_demo_bookings_demo_booking_id",
                        column: x => x.demo_booking_id,
                        principalTable: "demo_bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_demo_feedbacks_teacher_profiles_teacher_profile_id",
                        column: x => x.teacher_profile_id,
                        principalTable: "teacher_profiles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "demo_participants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    demo_booking_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    has_joined = table.Column<bool>(type: "boolean", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_demo_participants", x => x.id);
                    table.ForeignKey(
                        name: "fk_demo_participants_demo_bookings_demo_booking_id",
                        column: x => x.demo_booking_id,
                        principalTable: "demo_bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refunds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    processed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refunds", x => x.id);
                    table.ForeignKey(
                        name: "fk_refunds_payment_transactions_payment_transaction_id",
                        column: x => x.payment_transaction_id,
                        principalTable: "payment_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_actor_user_id",
                table: "audit_logs",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_name_entity_id",
                table: "audit_logs",
                columns: new[] { "entity_name", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_batch_enrollments_batch_id_child_id",
                table: "batch_enrollments",
                columns: new[] { "batch_id", "child_id" },
                unique: true,
                filter: "\"is_deleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "ix_batch_enrollments_child_id",
                table: "batch_enrollments",
                column: "child_id");

            migrationBuilder.CreateIndex(
                name: "ix_batches_course_id",
                table: "batches",
                column: "course_id");

            migrationBuilder.CreateIndex(
                name: "ix_batches_status",
                table: "batches",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_batches_teacher_profile_id",
                table: "batches",
                column: "teacher_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_children_parent_profile_id",
                table: "children",
                column: "parent_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_class_sessions_batch_id",
                table: "class_sessions",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_class_sessions_carried_forward_from_session_id",
                table: "class_sessions",
                column: "carried_forward_from_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_class_sessions_rescheduled_from_session_id",
                table: "class_sessions",
                column: "rescheduled_from_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_class_sessions_scheduled_start_at_utc",
                table: "class_sessions",
                column: "scheduled_start_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_class_sessions_status",
                table: "class_sessions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_class_sessions_teacher_profile_id",
                table: "class_sessions",
                column: "teacher_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_course_categories_name",
                table: "course_categories",
                column: "name",
                unique: true,
                filter: "\"is_deleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "ix_courses_course_category_id",
                table: "courses",
                column: "course_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_demo_bookings_class_session_id",
                table: "demo_bookings",
                column: "class_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_demo_bookings_conversion_status",
                table: "demo_bookings",
                column: "conversion_status");

            migrationBuilder.CreateIndex(
                name: "ix_demo_bookings_invoice_id",
                table: "demo_bookings",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_demo_feedbacks_demo_booking_id",
                table: "demo_feedbacks",
                column: "demo_booking_id",
                unique: true,
                filter: "\"is_deleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "ix_demo_feedbacks_recommended_course_id",
                table: "demo_feedbacks",
                column: "recommended_course_id");

            migrationBuilder.CreateIndex(
                name: "ix_demo_feedbacks_teacher_profile_id",
                table: "demo_feedbacks",
                column: "teacher_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_demo_participants_demo_booking_id",
                table: "demo_participants",
                column: "demo_booking_id");

            migrationBuilder.CreateIndex(
                name: "ix_enrollment_forms_child_id",
                table: "enrollment_forms",
                column: "child_id");

            migrationBuilder.CreateIndex(
                name: "ix_enrollment_forms_parent_profile_id",
                table: "enrollment_forms",
                column: "parent_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_fee_suspensions_invoice_id",
                table: "fee_suspensions",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_fee_suspensions_parent_profile_id_status",
                table: "fee_suspensions",
                columns: new[] { "parent_profile_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_holidays_date",
                table: "holidays",
                column: "date",
                unique: true,
                filter: "\"is_deleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_child_id",
                table: "invoices",
                column: "child_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_invoice_number",
                table: "invoices",
                column: "invoice_number",
                unique: true,
                filter: "\"is_deleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_parent_profile_id",
                table: "invoices",
                column: "parent_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_payment_account_id",
                table: "invoices",
                column: "payment_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_status_due_date",
                table: "invoices",
                columns: new[] { "status", "due_date" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_subscription_id",
                table: "invoices",
                column: "subscription_id");

            migrationBuilder.CreateIndex(
                name: "ix_leave_requests_teacher_profile_id_status",
                table: "leave_requests",
                columns: new[] { "teacher_profile_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_recipient_user_id_status",
                table: "notifications",
                columns: new[] { "recipient_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_package_plans_course_id",
                table: "package_plans",
                column: "course_id");

            migrationBuilder.CreateIndex(
                name: "ix_parent_profiles_user_id",
                table: "parent_profiles",
                column: "user_id",
                unique: true,
                filter: "\"is_deleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "ix_payment_accounts_department",
                table: "payment_accounts",
                column: "department",
                unique: true,
                filter: "\"is_deleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_gateway_transaction_id",
                table: "payment_transactions",
                column: "gateway_transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_invoice_id",
                table: "payment_transactions",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_transactions_payment_account_id",
                table: "payment_transactions",
                column: "payment_account_id");

            migrationBuilder.CreateIndex(
                name: "ix_payout_items_class_session_id",
                table: "payout_items",
                column: "class_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_payout_items_payout_id",
                table: "payout_items",
                column: "payout_id");

            migrationBuilder.CreateIndex(
                name: "ix_payout_rates_teacher_profile_id_duration_minutes_effective_",
                table: "payout_rates",
                columns: new[] { "teacher_profile_id", "duration_minutes", "effective_from" },
                unique: true,
                filter: "\"is_deleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "ix_payouts_teacher_profile_id_period_year_period_month",
                table: "payouts",
                columns: new[] { "teacher_profile_id", "period_year", "period_month" },
                unique: true,
                filter: "\"is_deleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "ix_refunds_payment_transaction_id",
                table: "refunds",
                column: "payment_transaction_id");

            migrationBuilder.CreateIndex(
                name: "ix_resource_accesses_parent_profile_id",
                table: "resource_accesses",
                column: "parent_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_resource_accesses_resource_id_parent_profile_id",
                table: "resource_accesses",
                columns: new[] { "resource_id", "parent_profile_id" },
                unique: true,
                filter: "\"is_deleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "ix_resources_batch_id",
                table: "resources",
                column: "batch_id");

            migrationBuilder.CreateIndex(
                name: "ix_resources_course_id",
                table: "resources",
                column: "course_id");

            migrationBuilder.CreateIndex(
                name: "ix_session_attendances_child_id",
                table: "session_attendances",
                column: "child_id");

            migrationBuilder.CreateIndex(
                name: "ix_session_attendances_class_session_id",
                table: "session_attendances",
                column: "class_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_session_attendances_class_session_id_child_id",
                table: "session_attendances",
                columns: new[] { "class_session_id", "child_id" },
                unique: true,
                filter: "\"child_id\" IS NOT NULL AND \"is_deleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "ix_session_attendances_class_session_id_teacher_profile_id",
                table: "session_attendances",
                columns: new[] { "class_session_id", "teacher_profile_id" },
                unique: true,
                filter: "\"teacher_profile_id\" IS NOT NULL AND \"is_deleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "ix_session_attendances_teacher_profile_id",
                table: "session_attendances",
                column: "teacher_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_session_recordings_class_session_id",
                table: "session_recordings",
                column: "class_session_id");

            migrationBuilder.CreateIndex(
                name: "ix_session_recordings_expires_at_utc",
                table: "session_recordings",
                column: "expires_at_utc");

            migrationBuilder.CreateIndex(
                name: "ix_sub_admin_permissions_user_id_module",
                table: "sub_admin_permissions",
                columns: new[] { "user_id", "module" },
                unique: true,
                filter: "\"is_deleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_child_id",
                table: "subscriptions",
                column: "child_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_package_plan_id",
                table: "subscriptions",
                column: "package_plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_parent_profile_id",
                table: "subscriptions",
                column: "parent_profile_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscriptions_status",
                table: "subscriptions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_teacher_profiles_user_id",
                table: "teacher_profiles",
                column: "user_id",
                unique: true,
                filter: "\"is_deleted\" = FALSE");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true,
                filter: "\"is_deleted\" = FALSE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "batch_enrollments");

            migrationBuilder.DropTable(
                name: "demo_feedbacks");

            migrationBuilder.DropTable(
                name: "demo_participants");

            migrationBuilder.DropTable(
                name: "enrollment_forms");

            migrationBuilder.DropTable(
                name: "fee_suspensions");

            migrationBuilder.DropTable(
                name: "holidays");

            migrationBuilder.DropTable(
                name: "leave_requests");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "payout_items");

            migrationBuilder.DropTable(
                name: "payout_rates");

            migrationBuilder.DropTable(
                name: "refunds");

            migrationBuilder.DropTable(
                name: "resource_accesses");

            migrationBuilder.DropTable(
                name: "session_attendances");

            migrationBuilder.DropTable(
                name: "session_recordings");

            migrationBuilder.DropTable(
                name: "sub_admin_permissions");

            migrationBuilder.DropTable(
                name: "demo_bookings");

            migrationBuilder.DropTable(
                name: "payouts");

            migrationBuilder.DropTable(
                name: "payment_transactions");

            migrationBuilder.DropTable(
                name: "resources");

            migrationBuilder.DropTable(
                name: "class_sessions");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "batches");

            migrationBuilder.DropTable(
                name: "payment_accounts");

            migrationBuilder.DropTable(
                name: "subscriptions");

            migrationBuilder.DropTable(
                name: "teacher_profiles");

            migrationBuilder.DropTable(
                name: "children");

            migrationBuilder.DropTable(
                name: "package_plans");

            migrationBuilder.DropTable(
                name: "parent_profiles");

            migrationBuilder.DropTable(
                name: "courses");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "course_categories");
        }
    }
}
