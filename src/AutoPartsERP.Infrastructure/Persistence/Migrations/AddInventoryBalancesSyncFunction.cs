using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// Unification step 2 (revised): keep inventory_balances reconciled from inventory_stock via a
/// periodic job instead of converting inventory_stock into a view.
///
/// The original plan (see UnifyItemsWithSkus) called for turning inventory_stock into a computed
/// view over inventory_balances. That was abandoned after reading PostInvoiceCommand: it issues
/// `SELECT ... FOR UPDATE` on inventory_stock to row-lock stock during invoice posting - the single
/// most sensitive write path in the system, since it decrements live stock and drives revenue. A
/// GROUP BY view (aggregating per-batch/status balances back into one on-hand figure) is not a
/// Postgres "simple" updatable view, so FOR UPDATE and the UPDATE that follows it would simply fail
/// there and in the 4 other sites that write inventory_stock directly (AdjustInventory,
/// ReceiveBatch, TransferStock). Rewriting all of those, untested, in the same session that just
/// finished tracking down a string of "never run against live infrastructure" bugs would be
/// repeating the mistake, not fixing it.
///
/// This is intentionally one-directional (inventory_stock -> inventory_balances) for now:
/// inventory_stock remains authoritative for the sales/invoicing path, inventory_balances is kept
/// caught up for the WMS screens that read it. Reconciling the other direction (WMS-driven writes
/// to inventory_balances via Receiving/Transfers/CycleCounts/StockAdjustments feeding back into
/// inventory_stock) is deferred to a later, separately-verified step.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000009_AddInventoryBalancesSyncFunction")]
public sealed class AddInventoryBalancesSyncFunction : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION sync_inventory_balances_from_stock() RETURNS void AS
            $$
            BEGIN
                -- NULL batch_id defeats a plain ON CONFLICT (Postgres unique constraints never treat
                -- two NULLs as equal), so this uses an explicit update-then-insert instead of upsert.

                UPDATE inventory_balances b
                SET qty = GREATEST(st.quantity_on_hand - st.quantity_reserved, 0),
                    updated_at = now()
                FROM inventory_stock st
                JOIN items i ON i.sku_id = st.sku_id
                WHERE b.item_id = i.id
                  AND b.location_id = st.location_id
                  AND b.batch_id IS NULL
                  AND b.status = 'AVAILABLE'
                  AND b.qty IS DISTINCT FROM GREATEST(st.quantity_on_hand - st.quantity_reserved, 0);

                INSERT INTO inventory_balances (id, item_id, location_id, batch_id, status, qty, updated_at)
                SELECT uuid_generate_v4(), i.id, st.location_id, NULL, 'AVAILABLE',
                       GREATEST(st.quantity_on_hand - st.quantity_reserved, 0), now()
                FROM inventory_stock st
                JOIN items i ON i.sku_id = st.sku_id
                WHERE NOT EXISTS (
                    SELECT 1 FROM inventory_balances b
                    WHERE b.item_id = i.id AND b.location_id = st.location_id
                      AND b.batch_id IS NULL AND b.status = 'AVAILABLE');

                UPDATE inventory_balances b
                SET qty = st.quantity_reserved,
                    updated_at = now()
                FROM inventory_stock st
                JOIN items i ON i.sku_id = st.sku_id
                WHERE b.item_id = i.id
                  AND b.location_id = st.location_id
                  AND b.batch_id IS NULL
                  AND b.status = 'RESERVED'
                  AND b.qty IS DISTINCT FROM st.quantity_reserved;

                INSERT INTO inventory_balances (id, item_id, location_id, batch_id, status, qty, updated_at)
                SELECT uuid_generate_v4(), i.id, st.location_id, NULL, 'RESERVED',
                       st.quantity_reserved, now()
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

        migrationBuilder.Sql("SELECT sync_inventory_balances_from_stock();");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS sync_inventory_balances_from_stock();");
    }
}
