using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// inventory_balances is unique on (item, location, batch, status), but Postgres treats two NULL batches as different, so every
/// "no batch" row was inserted again instead of added to (ON CONFLICT never fired) and quantities were split over many rows or,
/// after the periodic stock copy, multiplied. This merges the duplicates and makes the constraint treat NULLs as equal
/// (PostgreSQL 15+), so the existing ON CONFLICT ... DO UPDATE statements finally work.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000016_DeduplicateInventoryBalances")]
public sealed class DeduplicateInventoryBalances : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            -- Rows in flight (RECEIVING, IN_TRANSIT, ...) are genuine partial quantities: add them up into one row.
            WITH dup AS (
                SELECT item_id, location_id, batch_id, status, SUM(qty) AS total,
                       (array_agg(id ORDER BY updated_at DESC, id))[1] AS keep
                FROM inventory_balances
                WHERE status NOT IN ('AVAILABLE', 'RESERVED')
                GROUP BY item_id, location_id, batch_id, status
                HAVING count(*) > 1)
            UPDATE inventory_balances b SET qty = d.total FROM dup d WHERE b.id = d.keep;

            DELETE FROM inventory_balances b
            USING (SELECT id, row_number() OVER (PARTITION BY item_id, location_id, batch_id, status ORDER BY updated_at DESC, id) AS rn
                   FROM inventory_balances WHERE status NOT IN ('AVAILABLE', 'RESERVED')) x
            WHERE b.id = x.id AND x.rn > 1;

            -- AVAILABLE / RESERVED without a batch are a mirror of sellable stock (the periodic copy overwrote every duplicate with
            -- the same figure), so keep one row and let the copy below restore the right quantity.
            DELETE FROM inventory_balances b
            USING (SELECT id, row_number() OVER (PARTITION BY item_id, location_id, batch_id, status ORDER BY updated_at DESC, id) AS rn
                   FROM inventory_balances WHERE status IN ('AVAILABLE', 'RESERVED') AND batch_id IS NULL) x
            WHERE b.id = x.id AND x.rn > 1;

            ALTER TABLE inventory_balances DROP CONSTRAINT IF EXISTS inventory_balances_item_id_location_id_batch_id_status_key;
            ALTER TABLE inventory_balances
                ADD CONSTRAINT inventory_balances_item_id_location_id_batch_id_status_key
                UNIQUE NULLS NOT DISTINCT (item_id, location_id, batch_id, status);

            SELECT sync_inventory_balances_from_stock();
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
