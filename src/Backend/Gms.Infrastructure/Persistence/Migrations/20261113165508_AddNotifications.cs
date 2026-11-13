using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Gms.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notification_episodes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    object_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_episodes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_job_runs",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    trigger = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    result = table.Column<string>(type: "jsonb", nullable: false),
                    duration_ms = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_job_runs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "notification_options",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    due_soon_days = table.Column<int>(type: "integer", nullable: false),
                    schedule_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    retention_days = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_options", x => x.id);
                    table.CheckConstraint("ck_notification_options_due_soon", "due_soon_days BETWEEN 1 AND 14");
                    table.CheckConstraint("ck_notification_options_retention", "retention_days BETWEEN 30 AND 365");
                    table.CheckConstraint("ck_notification_options_single", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "notification_type_settings",
                columns: table => new
                {
                    type_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_type_settings", x => x.type_code);
                });

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    @params = table.Column<string>(name: "params", type: "jsonb", nullable: false),
                    target_kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    target_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notifications", x => x.id);
                    table.ForeignKey(
                        name: "FK_notifications_users_recipient_user_id",
                        column: x => x.recipient_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_notification_episodes_ended_at",
                table: "notification_episodes",
                column: "ended_at");

            migrationBuilder.CreateIndex(
                name: "ux_notification_episodes_active",
                table: "notification_episodes",
                columns: new[] { "type_code", "object_key" },
                unique: true,
                filter: "ended_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_notification_job_runs_started_at",
                table: "notification_job_runs",
                column: "started_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_notifications_created_at",
                table: "notifications",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_notifications_recipient_user_id_category_created_at",
                table: "notifications",
                columns: new[] { "recipient_user_id", "category", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_recipient_user_id_created_at",
                table: "notifications",
                columns: new[] { "recipient_user_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_notifications_recipient_user_id_source_key",
                table: "notifications",
                columns: new[] { "recipient_user_id", "source_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notifications_unread",
                table: "notifications",
                column: "recipient_user_id",
                filter: "read_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_episodes");

            migrationBuilder.DropTable(
                name: "notification_job_runs");

            migrationBuilder.DropTable(
                name: "notification_options");

            migrationBuilder.DropTable(
                name: "notification_type_settings");

            migrationBuilder.DropTable(
                name: "notifications");
        }
    }
}
