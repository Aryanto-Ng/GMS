using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Gms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,")
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateTable(
                name: "app_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "citext", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    event_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    target_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    target_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    old_value = table.Column<string>(type: "jsonb", nullable: true),
                    new_value = table.Column<string>(type: "jsonb", nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "colors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "citext", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    hex = table.Column<string>(type: "character(7)", fixedLength: true, maxLength: 7, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_colors", x => x.id);
                    table.CheckConstraint("ck_colors_hex", "hex IS NULL OR hex ~ '^#[0-9A-Fa-f]{6}$'");
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "citext", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "citext", maxLength: 200, nullable: false),
                    contact_person = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "material_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "citext", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "number_sequences",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    next_value = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_number_sequences", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "payroll_periods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "citext", maxLength: 30, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    total_paid = table.Column<decimal>(type: "numeric(18,0)", precision: 18, scale: 0, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payroll_periods", x => x.id);
                    table.CheckConstraint("ck_payroll_periods_dates", "end_date >= start_date");
                    table.CheckConstraint("ck_payroll_periods_status", "status IN ('Open','Closed')");
                });

            migrationBuilder.CreateTable(
                name: "permissions",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    resource = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    module = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    description = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_assignable = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permissions", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "processes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "citext", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processes", x => x.id);
                    table.CheckConstraint("ck_processes_type", "type IN ('Internal','Vendor')");
                });

            migrationBuilder.CreateTable(
                name: "sizes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "citext", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sizes", x => x.id);
                    table.CheckConstraint("ck_sizes_sort_order", "sort_order >= 0");
                });

            migrationBuilder.CreateTable(
                name: "uoms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "citext", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_uoms", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "vendors",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "citext", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    service_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    contact_person = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vendors", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    must_change_password = table.Column<bool>(type: "boolean", nullable: false),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.id);
                    table.ForeignKey(
                        name: "FK_users_app_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "app_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fgs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "citext", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fgs", x => x.id);
                    table.ForeignKey(
                        name: "FK_fgs_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "citext", maxLength: 30, nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_po_number = table.Column<string>(type: "citext", maxLength: 100, nullable: true),
                    po_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchase_orders", x => x.id);
                    table.CheckConstraint("ck_purchase_orders_status", "status IN ('Draft','Confirmed','InProgress','Completed','Cancelled')");
                    table.ForeignKey(
                        name: "FK_purchase_orders_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payroll_snapshot_lines",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    worker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    worker_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    worker_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    wo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wo_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    process_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    process_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    original_date = table.Column<DateOnly>(type: "date", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    qty = table.Column<int>(type: "integer", nullable: false),
                    rate = table.Column<decimal>(type: "numeric(18,5)", precision: 18, scale: 5, nullable: false),
                    subtotal = table.Column<decimal>(type: "numeric(28,5)", precision: 28, scale: 5, nullable: false),
                    source_entry_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payroll_snapshot_lines", x => x.id);
                    table.ForeignKey(
                        name: "FK_payroll_snapshot_lines_payroll_periods_period_id",
                        column: x => x.period_id,
                        principalTable: "payroll_periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "role_permissions",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_key = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_permissions", x => new { x.role_id, x.permission_key });
                    table.ForeignKey(
                        name: "FK_role_permissions_app_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "app_roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_role_permissions_permissions_permission_key",
                        column: x => x.permission_key,
                        principalTable: "permissions",
                        principalColumn: "key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "workers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "citext", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    join_date = table.Column<DateOnly>(type: "date", nullable: true),
                    main_process_id = table.Column<Guid>(type: "uuid", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_workers", x => x.id);
                    table.ForeignKey(
                        name: "FK_workers_processes_main_process_id",
                        column: x => x.main_process_id,
                        principalTable: "processes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "materials",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "citext", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    uom_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_materials", x => x.id);
                    table.ForeignKey(
                        name: "FK_materials_material_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "material_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_materials_uoms_uom_id",
                        column: x => x.uom_id,
                        principalTable: "uoms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_claims_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "FK_user_logins_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ip_address = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_user_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "user_tokens",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "FK_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "fg_routing_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    fg_id = table.Column<Guid>(type: "uuid", nullable: false),
                    process_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fg_routing_steps", x => x.id);
                    table.UniqueConstraint("ux_fg_routing_steps_fg_process", x => new { x.fg_id, x.process_id });
                    table.CheckConstraint("ck_fg_routing_steps_seq", "seq >= 1");
                    table.ForeignKey(
                        name: "FK_fg_routing_steps_fgs_fg_id",
                        column: x => x.fg_id,
                        principalTable: "fgs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_fg_routing_steps_processes_process_id",
                        column: x => x.process_id,
                        principalTable: "processes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fg_sizes",
                columns: table => new
                {
                    fg_id = table.Column<Guid>(type: "uuid", nullable: false),
                    size_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fg_sizes", x => new { x.fg_id, x.size_id });
                    table.ForeignKey(
                        name: "FK_fg_sizes_fgs_fg_id",
                        column: x => x.fg_id,
                        principalTable: "fgs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_fg_sizes_sizes_size_id",
                        column: x => x.size_id,
                        principalTable: "sizes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_balances",
                columns: table => new
                {
                    fg_id = table.Column<Guid>(type: "uuid", nullable: false),
                    color_id = table.Column<Guid>(type: "uuid", nullable: false),
                    size_id = table.Column<Guid>(type: "uuid", nullable: false),
                    on_hand = table.Column<int>(type: "integer", nullable: false),
                    allocated = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_balances", x => new { x.fg_id, x.color_id, x.size_id });
                    table.CheckConstraint("ck_stock_balances_valid", "on_hand >= 0 AND allocated >= 0 AND allocated <= on_hand");
                    table.ForeignKey(
                        name: "FK_stock_balances_colors_color_id",
                        column: x => x.color_id,
                        principalTable: "colors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_balances_fgs_fg_id",
                        column: x => x.fg_id,
                        principalTable: "fgs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_balances_sizes_size_id",
                        column: x => x.size_id,
                        principalTable: "sizes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_ledger",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    fg_id = table.Column<Guid>(type: "uuid", nullable: false),
                    color_id = table.Column<Guid>(type: "uuid", nullable: false),
                    size_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    qty = table.Column<int>(type: "integer", nullable: false),
                    source_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stock_ledger", x => x.id);
                    table.CheckConstraint("ck_stock_ledger_qty", "qty <> 0");
                    table.ForeignKey(
                        name: "FK_stock_ledger_colors_color_id",
                        column: x => x.color_id,
                        principalTable: "colors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_ledger_fgs_fg_id",
                        column: x => x.fg_id,
                        principalTable: "fgs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_ledger_sizes_size_id",
                        column: x => x.size_id,
                        principalTable: "sizes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "deliveries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "citext", maxLength: 30, nullable: false),
                    po_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_date = table.Column<DateOnly>(type: "date", nullable: false),
                    recipient_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    vehicle = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    driver = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    replaces_id = table.Column<Guid>(type: "uuid", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    print_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deliveries", x => x.id);
                    table.CheckConstraint("ck_deliveries_print_count", "print_count >= 0");
                    table.CheckConstraint("ck_deliveries_void", "status <> 'Voided' OR void_reason IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_deliveries_deliveries_replaces_id",
                        column: x => x.replaces_id,
                        principalTable: "deliveries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_deliveries_purchase_orders_po_id",
                        column: x => x.po_id,
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "po_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    po_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fg_id = table.Column<Guid>(type: "uuid", nullable: false),
                    color_id = table.Column<Guid>(type: "uuid", nullable: false),
                    size_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_date = table.Column<DateOnly>(type: "date", nullable: false),
                    qty = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    delivered_qty = table.Column<int>(type: "integer", nullable: false),
                    allocated_qty = table.Column<int>(type: "integer", nullable: false),
                    in_production_qty = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_po_lines", x => x.id);
                    table.CheckConstraint("ck_po_lines_counters_non_negative", "delivered_qty >= 0 AND allocated_qty >= 0 AND in_production_qty >= 0");
                    table.CheckConstraint("ck_po_lines_qty", "qty > 0 AND qty <= 1000000");
                    table.CheckConstraint("ck_po_lines_remaining_plan", "delivered_qty + allocated_qty + in_production_qty <= qty");
                    table.ForeignKey(
                        name: "FK_po_lines_colors_color_id",
                        column: x => x.color_id,
                        principalTable: "colors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_po_lines_fgs_fg_id",
                        column: x => x.fg_id,
                        principalTable: "fgs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_po_lines_purchase_orders_po_id",
                        column: x => x.po_id,
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_po_lines_sizes_size_id",
                        column: x => x.size_id,
                        principalTable: "sizes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "work_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "citext", maxLength: 30, nullable: false),
                    po_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fg_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fg_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    fg_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    target_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    released_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    released_by = table.Column<Guid>(type: "uuid", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_work_orders", x => x.id);
                    table.CheckConstraint("ck_work_orders_dates", "start_date IS NULL OR target_date IS NULL OR target_date >= start_date");
                    table.CheckConstraint("ck_work_orders_status", "status IN ('Draft','Released','InProgress','Completed','Closed','Cancelled')");
                    table.ForeignKey(
                        name: "FK_work_orders_fgs_fg_id",
                        column: x => x.fg_id,
                        principalTable: "fgs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_work_orders_purchase_orders_po_id",
                        column: x => x.po_id,
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payroll_snapshot_workers",
                columns: table => new
                {
                    period_id = table.Column<Guid>(type: "uuid", nullable: false),
                    worker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    worker_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    worker_name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    total_qty = table.Column<int>(type: "integer", nullable: false),
                    sum_before_rounding = table.Column<decimal>(type: "numeric(28,5)", precision: 28, scale: 5, nullable: false),
                    paid_amount = table.Column<decimal>(type: "numeric(18,0)", precision: 18, scale: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payroll_snapshot_workers", x => new { x.period_id, x.worker_id });
                    table.ForeignKey(
                        name: "FK_payroll_snapshot_workers_payroll_periods_period_id",
                        column: x => x.period_id,
                        principalTable: "payroll_periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_payroll_snapshot_workers_workers_worker_id",
                        column: x => x.worker_id,
                        principalTable: "workers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fg_tariffs",
                columns: table => new
                {
                    fg_id = table.Column<Guid>(type: "uuid", nullable: false),
                    process_id = table.Column<Guid>(type: "uuid", nullable: false),
                    size_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rate = table.Column<decimal>(type: "numeric(18,5)", precision: 18, scale: 5, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fg_tariffs", x => new { x.fg_id, x.process_id, x.size_id });
                    table.CheckConstraint("ck_fg_tariffs_rate", "rate > 0");
                    table.ForeignKey(
                        name: "FK_fg_tariffs_fg_routing_steps_fg_id_process_id",
                        columns: x => new { x.fg_id, x.process_id },
                        principalTable: "fg_routing_steps",
                        principalColumns: new[] { "fg_id", "process_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_fg_tariffs_fg_sizes_fg_id_size_id",
                        columns: x => new { x.fg_id, x.size_id },
                        principalTable: "fg_sizes",
                        principalColumns: new[] { "fg_id", "size_id" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_fg_tariffs_fgs_fg_id",
                        column: x => x.fg_id,
                        principalTable: "fgs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "delivery_lines",
                columns: table => new
                {
                    delivery_id = table.Column<Guid>(type: "uuid", nullable: false),
                    po_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    qty = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_lines", x => new { x.delivery_id, x.po_line_id });
                    table.CheckConstraint("ck_delivery_lines_qty", "qty > 0");
                    table.ForeignKey(
                        name: "FK_delivery_lines_deliveries_delivery_id",
                        column: x => x.delivery_id,
                        principalTable: "deliveries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_delivery_lines_po_lines_po_line_id",
                        column: x => x.po_line_id,
                        principalTable: "po_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "po_allocations",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    po_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    qty = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_po_allocations", x => x.id);
                    table.CheckConstraint("ck_po_allocations_qty", "qty > 0");
                    table.ForeignKey(
                        name: "FK_po_allocations_po_lines_po_line_id",
                        column: x => x.po_line_id,
                        principalTable: "po_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fg_handovers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handover_date = table.Column<DateOnly>(type: "date", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    received_by = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    replaces_id = table.Column<Guid>(type: "uuid", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fg_handovers", x => x.id);
                    table.CheckConstraint("ck_fg_handovers_void", "status <> 'Voided' OR void_reason IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_fg_handovers_fg_handovers_replaces_id",
                        column: x => x.replaces_id,
                        principalTable: "fg_handovers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fg_handovers_work_orders_wo_id",
                        column: x => x.wo_id,
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "material_handovers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handover_date = table.Column<DateOnly>(type: "date", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_handovers", x => x.id);
                    table.CheckConstraint("ck_material_handovers_void", "status <> 'Voided' OR void_reason IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_material_handovers_work_orders_wo_id",
                        column: x => x.wo_id,
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wo_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    po_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    qty = table.Column<int>(type: "integer", nullable: false),
                    handed_over_qty = table.Column<int>(type: "integer", nullable: false),
                    color_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    color_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    size_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wo_lines", x => x.id);
                    table.CheckConstraint("ck_wo_lines_handed_over", "handed_over_qty >= 0");
                    table.CheckConstraint("ck_wo_lines_qty", "qty > 0");
                    table.ForeignKey(
                        name: "FK_wo_lines_po_lines_po_line_id",
                        column: x => x.po_line_id,
                        principalTable: "po_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_wo_lines_work_orders_wo_id",
                        column: x => x.wo_id,
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wo_routing_snapshot",
                columns: table => new
                {
                    wo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    process_id = table.Column<Guid>(type: "uuid", nullable: false),
                    process_code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    process_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    process_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wo_routing_snapshot", x => new { x.wo_id, x.seq });
                    table.ForeignKey(
                        name: "FK_wo_routing_snapshot_processes_process_id",
                        column: x => x.process_id,
                        principalTable: "processes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_wo_routing_snapshot_work_orders_wo_id",
                        column: x => x.wo_id,
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wo_status_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    to_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wo_status_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_wo_status_history_work_orders_wo_id",
                        column: x => x.wo_id,
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "material_handover_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    handover_id = table.Column<Guid>(type: "uuid", nullable: false),
                    material_id = table.Column<Guid>(type: "uuid", nullable: false),
                    qty = table.Column<decimal>(type: "numeric(18,3)", precision: 18, scale: 3, nullable: false),
                    uom_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_material_handover_lines", x => x.id);
                    table.CheckConstraint("ck_material_handover_lines_qty", "qty > 0");
                    table.ForeignKey(
                        name: "FK_material_handover_lines_material_handovers_handover_id",
                        column: x => x.handover_id,
                        principalTable: "material_handovers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_material_handover_lines_materials_material_id",
                        column: x => x.material_id,
                        principalTable: "materials",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fg_handover_lines",
                columns: table => new
                {
                    handover_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wo_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    qty = table.Column<int>(type: "integer", nullable: false),
                    auto_allocated_qty = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_fg_handover_lines", x => new { x.handover_id, x.wo_line_id });
                    table.CheckConstraint("ck_fg_handover_lines_auto_allocated", "auto_allocated_qty >= 0 AND auto_allocated_qty <= qty");
                    table.CheckConstraint("ck_fg_handover_lines_qty", "qty > 0");
                    table.ForeignKey(
                        name: "FK_fg_handover_lines_fg_handovers_handover_id",
                        column: x => x.handover_id,
                        principalTable: "fg_handovers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_fg_handover_lines_wo_lines_wo_line_id",
                        column: x => x.wo_line_id,
                        principalTable: "wo_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wo_step_balances",
                columns: table => new
                {
                    wo_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    qty = table.Column<int>(type: "integer", nullable: false),
                    sent_qty = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wo_step_balances", x => new { x.wo_line_id, x.seq });
                    table.CheckConstraint("ck_wo_step_balances_non_negative", "qty >= 0 AND sent_qty >= 0");
                    table.ForeignKey(
                        name: "FK_wo_step_balances_wo_lines_wo_line_id",
                        column: x => x.wo_line_id,
                        principalTable: "wo_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "output_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    worker_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    original_date = table.Column<DateOnly>(type: "date", nullable: true),
                    replaces_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ref_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_output_entries", x => x.id);
                    table.CheckConstraint("ck_output_entries_adjustment", "kind <> 'Adjustment' OR (original_date IS NOT NULL AND reason IS NOT NULL)");
                    table.CheckConstraint("ck_output_entries_void", "status <> 'Voided' OR void_reason IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_output_entries_output_entries_ref_entry_id",
                        column: x => x.ref_entry_id,
                        principalTable: "output_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_output_entries_output_entries_replaces_entry_id",
                        column: x => x.replaces_entry_id,
                        principalTable: "output_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_output_entries_wo_routing_snapshot_wo_id_seq",
                        columns: x => new { x.wo_id, x.seq },
                        principalTable: "wo_routing_snapshot",
                        principalColumns: new[] { "wo_id", "seq" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_output_entries_workers_worker_id",
                        column: x => x.worker_id,
                        principalTable: "workers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vendor_movements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    wo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    movement_date = table.Column<DateOnly>(type: "date", nullable: false),
                    doc_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    replaces_id = table.Column<Guid>(type: "uuid", nullable: true),
                    void_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    voided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    voided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vendor_movements", x => x.id);
                    table.CheckConstraint("ck_vendor_movements_void", "status <> 'Voided' OR void_reason IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_vendor_movements_vendor_movements_replaces_id",
                        column: x => x.replaces_id,
                        principalTable: "vendor_movements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vendor_movements_vendors_vendor_id",
                        column: x => x.vendor_id,
                        principalTable: "vendors",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_vendor_movements_wo_routing_snapshot_wo_id_seq",
                        columns: x => new { x.wo_id, x.seq },
                        principalTable: "wo_routing_snapshot",
                        principalColumns: new[] { "wo_id", "seq" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "wo_tariff_snapshot",
                columns: table => new
                {
                    wo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    size_id = table.Column<Guid>(type: "uuid", nullable: false),
                    size_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    rate = table.Column<decimal>(type: "numeric(18,5)", precision: 18, scale: 5, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wo_tariff_snapshot", x => new { x.wo_id, x.seq, x.size_id });
                    table.CheckConstraint("ck_wo_tariff_snapshot_rate", "rate > 0");
                    table.ForeignKey(
                        name: "FK_wo_tariff_snapshot_wo_routing_snapshot_wo_id_seq",
                        columns: x => new { x.wo_id, x.seq },
                        principalTable: "wo_routing_snapshot",
                        principalColumns: new[] { "wo_id", "seq" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wo_tariff_snapshot_work_orders_wo_id",
                        column: x => x.wo_id,
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "output_entry_lines",
                columns: table => new
                {
                    entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wo_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    qty = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_output_entry_lines", x => new { x.entry_id, x.wo_line_id });
                    table.CheckConstraint("ck_output_entry_lines_qty", "qty <> 0");
                    table.ForeignKey(
                        name: "FK_output_entry_lines_output_entries_entry_id",
                        column: x => x.entry_id,
                        principalTable: "output_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_output_entry_lines_wo_lines_wo_line_id",
                        column: x => x.wo_line_id,
                        principalTable: "wo_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "vendor_movement_lines",
                columns: table => new
                {
                    movement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    wo_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    qty = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vendor_movement_lines", x => new { x.movement_id, x.wo_line_id });
                    table.CheckConstraint("ck_vendor_movement_lines_qty", "qty > 0");
                    table.ForeignKey(
                        name: "FK_vendor_movement_lines_vendor_movements_movement_id",
                        column: x => x.movement_id,
                        principalTable: "vendor_movements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_vendor_movement_lines_wo_lines_wo_line_id",
                        column: x => x.wo_line_id,
                        principalTable: "wo_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_app_roles_name",
                table: "app_roles",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_actor_user_id_occurred_at",
                table: "audit_logs",
                columns: new[] { "actor_user_id", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_event_type_occurred_at",
                table: "audit_logs",
                columns: new[] { "event_type", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_occurred_at",
                table: "audit_logs",
                column: "occurred_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_colors_code",
                table: "colors",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customers_code",
                table: "customers",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_customers_name",
                table: "customers",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_code",
                table: "deliveries",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_delivery_date",
                table: "deliveries",
                column: "delivery_date",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_po_id",
                table: "deliveries",
                column: "po_id");

            migrationBuilder.CreateIndex(
                name: "IX_deliveries_replaces_id",
                table: "deliveries",
                column: "replaces_id");

            migrationBuilder.CreateIndex(
                name: "IX_delivery_lines_po_line_id",
                table: "delivery_lines",
                column: "po_line_id");

            migrationBuilder.CreateIndex(
                name: "IX_fg_handover_lines_wo_line_id",
                table: "fg_handover_lines",
                column: "wo_line_id");

            migrationBuilder.CreateIndex(
                name: "IX_fg_handovers_handover_date",
                table: "fg_handovers",
                column: "handover_date",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_fg_handovers_replaces_id",
                table: "fg_handovers",
                column: "replaces_id");

            migrationBuilder.CreateIndex(
                name: "IX_fg_handovers_wo_id",
                table: "fg_handovers",
                column: "wo_id");

            migrationBuilder.CreateIndex(
                name: "IX_fg_routing_steps_process_id",
                table: "fg_routing_steps",
                column: "process_id");

            migrationBuilder.CreateIndex(
                name: "IX_fg_sizes_size_id",
                table: "fg_sizes",
                column: "size_id");

            migrationBuilder.CreateIndex(
                name: "IX_fg_tariffs_fg_id_size_id",
                table: "fg_tariffs",
                columns: new[] { "fg_id", "size_id" });

            migrationBuilder.CreateIndex(
                name: "IX_fgs_code",
                table: "fgs",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fgs_customer_id",
                table: "fgs",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_categories_code",
                table: "material_categories",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_material_handover_lines_handover_id",
                table: "material_handover_lines",
                column: "handover_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_handover_lines_material_id",
                table: "material_handover_lines",
                column: "material_id");

            migrationBuilder.CreateIndex(
                name: "IX_material_handovers_wo_id",
                table: "material_handovers",
                column: "wo_id");

            migrationBuilder.CreateIndex(
                name: "IX_materials_category_id",
                table: "materials",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "IX_materials_code",
                table: "materials",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_materials_uom_id",
                table: "materials",
                column: "uom_id");

            migrationBuilder.CreateIndex(
                name: "ix_output_entries_payroll",
                table: "output_entries",
                columns: new[] { "work_date", "worker_id" },
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_output_entries_ref_entry_id",
                table: "output_entries",
                column: "ref_entry_id");

            migrationBuilder.CreateIndex(
                name: "IX_output_entries_replaces_entry_id",
                table: "output_entries",
                column: "replaces_entry_id");

            migrationBuilder.CreateIndex(
                name: "IX_output_entries_wo_id_seq_status",
                table: "output_entries",
                columns: new[] { "wo_id", "seq", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_output_entries_worker_id_work_date",
                table: "output_entries",
                columns: new[] { "worker_id", "work_date" });

            migrationBuilder.CreateIndex(
                name: "IX_output_entry_lines_wo_line_id",
                table: "output_entry_lines",
                column: "wo_line_id");

            migrationBuilder.CreateIndex(
                name: "IX_payroll_periods_code",
                table: "payroll_periods",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_payroll_periods_start_date_end_date",
                table: "payroll_periods",
                columns: new[] { "start_date", "end_date" });

            migrationBuilder.CreateIndex(
                name: "ux_payroll_periods_single_open",
                table: "payroll_periods",
                column: "status",
                unique: true,
                filter: "status = 'Open'");

            migrationBuilder.CreateIndex(
                name: "IX_payroll_snapshot_lines_period_id_worker_id_work_date",
                table: "payroll_snapshot_lines",
                columns: new[] { "period_id", "worker_id", "work_date" });

            migrationBuilder.CreateIndex(
                name: "IX_payroll_snapshot_workers_worker_id",
                table: "payroll_snapshot_workers",
                column: "worker_id");

            migrationBuilder.CreateIndex(
                name: "IX_permissions_sort_order",
                table: "permissions",
                column: "sort_order");

            migrationBuilder.CreateIndex(
                name: "IX_po_allocations_po_line_id_id",
                table: "po_allocations",
                columns: new[] { "po_line_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_po_lines_color_id",
                table: "po_lines",
                column: "color_id");

            migrationBuilder.CreateIndex(
                name: "IX_po_lines_fg_id_color_id_size_id",
                table: "po_lines",
                columns: new[] { "fg_id", "color_id", "size_id" });

            migrationBuilder.CreateIndex(
                name: "ix_po_lines_open_delivery_date",
                table: "po_lines",
                column: "delivery_date",
                filter: "delivered_qty < qty");

            migrationBuilder.CreateIndex(
                name: "IX_po_lines_po_id",
                table: "po_lines",
                column: "po_id");

            migrationBuilder.CreateIndex(
                name: "IX_po_lines_size_id",
                table: "po_lines",
                column: "size_id");

            migrationBuilder.CreateIndex(
                name: "ux_po_lines_combination",
                table: "po_lines",
                columns: new[] { "po_id", "fg_id", "color_id", "size_id", "delivery_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_processes_code",
                table: "processes",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_code",
                table: "purchase_orders",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_customer_id",
                table: "purchase_orders",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_po_date",
                table: "purchase_orders",
                column: "po_date",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_purchase_orders_status",
                table: "purchase_orders",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_purchase_orders_customer_po_number",
                table: "purchase_orders",
                columns: new[] { "customer_id", "customer_po_number" },
                unique: true,
                filter: "customer_po_number IS NOT NULL AND status <> 'Cancelled'");

            migrationBuilder.CreateIndex(
                name: "IX_role_permissions_permission_key",
                table: "role_permissions",
                column: "permission_key");

            migrationBuilder.CreateIndex(
                name: "IX_sizes_code",
                table: "sizes",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sizes_sort_order",
                table: "sizes",
                column: "sort_order");

            migrationBuilder.CreateIndex(
                name: "IX_stock_balances_color_id",
                table: "stock_balances",
                column: "color_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_balances_size_id",
                table: "stock_balances",
                column: "size_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_ledger_color_id",
                table: "stock_ledger",
                column: "color_id");

            migrationBuilder.CreateIndex(
                name: "IX_stock_ledger_fg_id_color_id_size_id_id",
                table: "stock_ledger",
                columns: new[] { "fg_id", "color_id", "size_id", "id" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_ledger_size_id",
                table: "stock_ledger",
                column: "size_id");

            migrationBuilder.CreateIndex(
                name: "IX_uoms_code",
                table: "uoms",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_claims_user_id",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_logins_user_id",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_sessions_token_hash",
                table: "user_sessions",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_user_sessions_active_per_user",
                table: "user_sessions",
                column: "user_id",
                unique: true,
                filter: "revoked_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "IX_users_role_id",
                table: "users",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "users",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_vendor_movement_lines_wo_line_id",
                table: "vendor_movement_lines",
                column: "wo_line_id");

            migrationBuilder.CreateIndex(
                name: "IX_vendor_movements_replaces_id",
                table: "vendor_movements",
                column: "replaces_id");

            migrationBuilder.CreateIndex(
                name: "IX_vendor_movements_vendor_id",
                table: "vendor_movements",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "IX_vendor_movements_wo_id_seq_vendor_id_status",
                table: "vendor_movements",
                columns: new[] { "wo_id", "seq", "vendor_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_vendors_code",
                table: "vendors",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wo_lines_po_line_id",
                table: "wo_lines",
                column: "po_line_id");

            migrationBuilder.CreateIndex(
                name: "IX_wo_lines_wo_id_po_line_id",
                table: "wo_lines",
                columns: new[] { "wo_id", "po_line_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wo_routing_snapshot_process_id",
                table: "wo_routing_snapshot",
                column: "process_id");

            migrationBuilder.CreateIndex(
                name: "IX_wo_status_history_wo_id_changed_at",
                table: "wo_status_history",
                columns: new[] { "wo_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "IX_work_orders_code",
                table: "work_orders",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_work_orders_fg_id",
                table: "work_orders",
                column: "fg_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_open_target_date",
                table: "work_orders",
                column: "target_date",
                filter: "status IN ('Released','InProgress','Completed')");

            migrationBuilder.CreateIndex(
                name: "IX_work_orders_po_id",
                table: "work_orders",
                column: "po_id");

            migrationBuilder.CreateIndex(
                name: "IX_work_orders_status",
                table: "work_orders",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_workers_code",
                table: "workers",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_workers_main_process_id",
                table: "workers",
                column: "main_process_id");

            migrationBuilder.CreateIndex(
                name: "IX_workers_name",
                table: "workers",
                column: "name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "delivery_lines");

            migrationBuilder.DropTable(
                name: "fg_handover_lines");

            migrationBuilder.DropTable(
                name: "fg_tariffs");

            migrationBuilder.DropTable(
                name: "material_handover_lines");

            migrationBuilder.DropTable(
                name: "number_sequences");

            migrationBuilder.DropTable(
                name: "output_entry_lines");

            migrationBuilder.DropTable(
                name: "payroll_snapshot_lines");

            migrationBuilder.DropTable(
                name: "payroll_snapshot_workers");

            migrationBuilder.DropTable(
                name: "po_allocations");

            migrationBuilder.DropTable(
                name: "role_permissions");

            migrationBuilder.DropTable(
                name: "stock_balances");

            migrationBuilder.DropTable(
                name: "stock_ledger");

            migrationBuilder.DropTable(
                name: "user_claims");

            migrationBuilder.DropTable(
                name: "user_logins");

            migrationBuilder.DropTable(
                name: "user_sessions");

            migrationBuilder.DropTable(
                name: "user_tokens");

            migrationBuilder.DropTable(
                name: "vendor_movement_lines");

            migrationBuilder.DropTable(
                name: "wo_status_history");

            migrationBuilder.DropTable(
                name: "wo_step_balances");

            migrationBuilder.DropTable(
                name: "wo_tariff_snapshot");

            migrationBuilder.DropTable(
                name: "deliveries");

            migrationBuilder.DropTable(
                name: "fg_handovers");

            migrationBuilder.DropTable(
                name: "fg_routing_steps");

            migrationBuilder.DropTable(
                name: "fg_sizes");

            migrationBuilder.DropTable(
                name: "material_handovers");

            migrationBuilder.DropTable(
                name: "materials");

            migrationBuilder.DropTable(
                name: "output_entries");

            migrationBuilder.DropTable(
                name: "payroll_periods");

            migrationBuilder.DropTable(
                name: "permissions");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "vendor_movements");

            migrationBuilder.DropTable(
                name: "wo_lines");

            migrationBuilder.DropTable(
                name: "material_categories");

            migrationBuilder.DropTable(
                name: "uoms");

            migrationBuilder.DropTable(
                name: "workers");

            migrationBuilder.DropTable(
                name: "app_roles");

            migrationBuilder.DropTable(
                name: "vendors");

            migrationBuilder.DropTable(
                name: "wo_routing_snapshot");

            migrationBuilder.DropTable(
                name: "po_lines");

            migrationBuilder.DropTable(
                name: "processes");

            migrationBuilder.DropTable(
                name: "work_orders");

            migrationBuilder.DropTable(
                name: "colors");

            migrationBuilder.DropTable(
                name: "sizes");

            migrationBuilder.DropTable(
                name: "fgs");

            migrationBuilder.DropTable(
                name: "purchase_orders");

            migrationBuilder.DropTable(
                name: "customers");
        }
    }
}
