using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chubb.Claims.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ClaimHistoryAppendOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION claims.block_history_change() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'claim_history is append-only (% blocked)', TG_OP
                        USING ERRCODE = 'restrict_violation';
                END;
                $$;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_claim_history_block_row_change
                BEFORE UPDATE OR DELETE ON claims.claim_history
                FOR EACH ROW EXECUTE FUNCTION claims.block_history_change();
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_claim_history_block_truncate
                BEFORE TRUNCATE ON claims.claim_history
                FOR EACH STATEMENT EXECUTE FUNCTION claims.block_history_change();
                """);
            // A foreign key would lock the referenced row, which needs the UPDATE privilege revoked below.
            migrationBuilder.Sql("""
                CREATE FUNCTION claims.check_referenced_entry() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.referenced_entry_id IS NOT NULL AND NOT EXISTS (
                        SELECT 1 FROM claims.claim_history
                        WHERE id = NEW.referenced_entry_id AND claim_id = NEW.claim_id) THEN
                        RAISE EXCEPTION 'referenced_entry_id must name an entry of the same claim'
                            USING ERRCODE = 'foreign_key_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_claim_history_check_referenced_entry
                BEFORE INSERT ON claims.claim_history
                FOR EACH ROW EXECUTE FUNCTION claims.check_referenced_entry();
                """);
            // The current role, not a fixed name, so compose, Testcontainers and production roles all work.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    EXECUTE format('REVOKE UPDATE, DELETE, TRUNCATE ON claims.claim_history FROM %I', current_user);
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    EXECUTE format('GRANT UPDATE, DELETE, TRUNCATE ON claims.claim_history TO %I', current_user);
                END $$;
                """);
            migrationBuilder.Sql("DROP TRIGGER trg_claim_history_check_referenced_entry ON claims.claim_history;");
            migrationBuilder.Sql("DROP FUNCTION claims.check_referenced_entry();");
            migrationBuilder.Sql("DROP TRIGGER trg_claim_history_block_truncate ON claims.claim_history;");
            migrationBuilder.Sql("DROP TRIGGER trg_claim_history_block_row_change ON claims.claim_history;");
            migrationBuilder.Sql("DROP FUNCTION claims.block_history_change();");
        }
    }
}
