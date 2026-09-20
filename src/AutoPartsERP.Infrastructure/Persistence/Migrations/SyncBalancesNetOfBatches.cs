using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// Stock is now kept in step with the warehouse documents at the moment they post (see StockLevelWriter), so the periodic
/// stock -> balances copy must not disagree with them. Its "no batch" AVAILABLE row used to be set to the WHOLE stock of a
/// location, which counted batch-tracked quantities twice (once in their batch rows, once here). It now holds only what the
/// batch rows do not already account for.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000015_SyncBalancesNetOfBatches")]
public sealed class SyncBalancesNetOfBatches : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION sync_inventory_balances_from_stock() RETURNS void AS
            $$
            BEGIN
                WITH wanted AS (
                    SELECT i.id AS item_id, st.location_id,
                           GREATEST(st.quantity_on_hand - st.quantity_reserved
                                    - COALESCE((SELECT SUM(b.qty) FROM inventory_balances b
                                                WHERE b.item_id = i.id AND b.location_id = st.location_id
                                                  AND b.batch_id IS NOT NULL AND b.status = 'AVAILABLE'), 0), 0) AS qty
                    FROM inventory_stock st
                    JOIN items i ON i.sku_id = st.sku_id)
                UPDATE inventory_balances b
                SET qty = w.qty, updated_at = now()
                FROM wanted w
                WHERE b.item_id = w.item_id AND b.location_id = w.location_id
                  AND b.batch_id IS NULL AND b.status = 'AVAILABLE'
                  AND b.qty IS DISTINCT FROM w.qty;

                INSERT INTO inventory_balances (id, item_id, location_id, batch_id, status, qty, updated_at)
                SELECT uuid_generate_v4(), i.id, st.location_id, NULL, 'AVAILABLE',
                       GREATEST(st.quantity_on_hand - st.quantity_reserved
                                - COALESCE((SELECT SUM(b.qty) FROM inventory_balances b
                                            WHERE b.item_id = i.id AND b.location_id = st.location_id
                                              AND b.batch_id IS NOT NULL AND b.status = 'AVAILABLE'), 0), 0), now()
                FROM inventory_stock st
                JOIN items i ON i.sku_id = st.sku_id
                WHERE NOT EXISTS (
                    SELECT 1 FROM inventory_balances b
                    WHERE b.item_id = i.id AND b.location_id = st.location_id
                      AND b.batch_id IS NULL AND b.status = 'AVAILABLE');

                UPDATE inventory_balances b
                SET qty = st.quantity_reserved, updated_at = now()
                FROM inventory_stock st
                JOIN items i ON i.sku_id = st.sku_id
                WHERE b.item_id = i.id AND b.location_id = st.location_id
                  AND b.batch_id IS NULL AND b.status = 'RESERVED'
                  AND b.qty IS DISTINCT FROM st.quantity_reserved;

                INSERT INTO inventory_balances (id, item_id, location_id, batch_id, status, qty, updated_at)
                SELECT uuid_generate_v4(), i.id, st.location_id, NULL, 'RESERVED', st.quantity_reserved, now()
                FROM inventory_stock st
                JOIN items i ON i.sku_id = st.sku_id
                WHERE st.quantity_reserved > 0
                  AND NOT EXISTS (
                    SELECT 1 FROM inventory_balances b
                    WHERE b.item_id = i.id AND b.location_id = st.location_id
                      AND b.batch_id IS NULL AND b.status = 'RESERVED');
            END;
            $$ LANGUAGE plpgsql;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
