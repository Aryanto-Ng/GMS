using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Gms.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Constraint yang tidak dapat dinyatakan lewat Fluent API (ADR-004, ADR-005, Database.md bagian 11):
    /// 1) unik (fg_id, seq) DEFERRABLE agar urutan routing dapat ditukar dalam satu transaksi,
    /// 2) exclusion constraint: periode gaji tidak boleh tumpang tindih,
    /// 3) audit_logs tidak dapat diubah/dihapus (immutability).
    /// </summary>
    public partial class AdvancedConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Routing: urutan unik per FG, DEFERRABLE (dicek saat commit).
            migrationBuilder.Sql("""
                ALTER TABLE fg_routing_steps
                    ADD CONSTRAINT ux_fg_routing_steps_fg_seq UNIQUE (fg_id, seq) DEFERRABLE INITIALLY DEFERRED;
                """);

            // 2) Periode gaji tanpa tumpang tindih (rentang tanggal inklusif). Membutuhkan ekstensi btree_gist.
            migrationBuilder.Sql("""
                ALTER TABLE payroll_periods
                    ADD CONSTRAINT ex_payroll_periods_no_overlap
                    EXCLUDE USING gist (daterange(start_date, end_date, '[]') WITH &&);
                """);

            // 3) Audit log append-only.
            migrationBuilder.Sql("""
                CREATE FUNCTION audit_logs_immutable() RETURNS trigger AS $$
                BEGIN
                    RAISE EXCEPTION 'audit_logs bersifat append-only: % tidak diizinkan', TG_OP
                        USING ERRCODE = 'integrity_constraint_violation';
                END;
                $$ LANGUAGE plpgsql;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_audit_logs_no_update_delete
                    BEFORE UPDATE OR DELETE ON audit_logs
                    FOR EACH ROW EXECUTE FUNCTION audit_logs_immutable();
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_audit_logs_no_truncate
                    BEFORE TRUNCATE ON audit_logs
                    FOR EACH STATEMENT EXECUTE FUNCTION audit_logs_immutable();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_logs_no_truncate ON audit_logs;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_logs_no_update_delete ON audit_logs;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit_logs_immutable();");
            migrationBuilder.Sql("ALTER TABLE payroll_periods DROP CONSTRAINT IF EXISTS ex_payroll_periods_no_overlap;");
            migrationBuilder.Sql("ALTER TABLE fg_routing_steps DROP CONSTRAINT IF EXISTS ux_fg_routing_steps_fg_seq;");
        }
    }
}
