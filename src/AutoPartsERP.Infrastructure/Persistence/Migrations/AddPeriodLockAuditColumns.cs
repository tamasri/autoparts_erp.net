using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// <c>period_locks</c> (created via raw SQL in <see cref="AddGovernanceTables"/>) never had
/// <c>created_at_utc</c>/<c>updated_at_utc</c> columns, but the EF <see cref="AutoPartsERP.Domain.Governance.PeriodLock"/>
/// entity (via its <c>AuditableEntity</c> base) has always expected them - causing
/// "column p.created_at_utc does not exist" on every read. Additive and idempotent.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000007_AddPeriodLockAuditColumns")]
public sealed class AddPeriodLockAuditColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE period_locks
                ADD COLUMN IF NOT EXISTS created_at_utc timestamptz,
                ADD COLUMN IF NOT EXISTS updated_at_utc timestamptz;
            """);

        migrationBuilder.Sql(
            "UPDATE period_locks SET created_at_utc = locked_at_utc WHERE created_at_utc IS NULL;");

        migrationBuilder.Sql(
            "ALTER TABLE period_locks ALTER COLUMN created_at_utc SET NOT NULL;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Additive/backfill migration - intentionally not reversed.
    }
}
