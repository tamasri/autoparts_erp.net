using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// A voided invoice / reversed payment is cancelled in ERPNext, which needs its own state in erpnext_sync_log
/// (CANCELLED) besides PENDING/SYNCED/FAILED/SKIPPED.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000013_AllowErpNextCancelledStatus")]
public sealed class AllowErpNextCancelledStatus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE erpnext_sync_log DROP CONSTRAINT IF EXISTS erpnext_sync_log_status_check;
            ALTER TABLE erpnext_sync_log ADD CONSTRAINT erpnext_sync_log_status_check
                CHECK (status IN ('PENDING','SYNCED','FAILED','SKIPPED','CANCELLED'));
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
