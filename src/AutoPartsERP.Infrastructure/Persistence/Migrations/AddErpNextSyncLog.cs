using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// Tracks the accounting hand-off to ERPNext decided for this project: this table lets the
/// (currently no-op) sync calls record what would have been sent and, once a real ERPNext
/// instance exists, gives a "Sync Status" screen something to show and retry from - each row
/// records the local entity, the target ERPNext doctype, its remote name once synced, and the
/// last error if the attempt failed.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000010_AddErpNextSyncLog")]
public sealed class AddErpNextSyncLog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS erpnext_sync_log (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                local_entity_type text NOT NULL,
                local_entity_id uuid NOT NULL,
                erpnext_doctype text NOT NULL,
                erpnext_name text NULL,
                status text NOT NULL DEFAULT 'PENDING' CHECK (status IN ('PENDING','SYNCED','FAILED','SKIPPED')),
                last_error text NULL,
                attempt_count int NOT NULL DEFAULT 0,
                synced_at timestamptz NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                updated_at timestamptz NULL,
                UNIQUE (local_entity_type, local_entity_id, erpnext_doctype)
            );
            CREATE INDEX IF NOT EXISTS idx_erpnext_sync_log_status ON erpnext_sync_log(status);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS erpnext_sync_log;");
    }
}
