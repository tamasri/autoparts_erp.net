using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// Stock alerts:
/// <list type="bullet">
/// <item>One reorder level. The item card edits <c>items.reorder_level</c>, but the dashboard, stock list and inventory-value report
/// read <c>skus.reorder_level</c>, which never changed after the SKU was created. A trigger now copies every item change to its SKU,
/// and the existing values are aligned once.</item>
/// <item>At most one open alert per item and alert type (partial unique index), so two scans running together cannot duplicate it.</item>
/// </list>
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000025_AddStockAlertSupport")]
public sealed class AddStockAlertSupport : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION items_reorder_level_to_sku() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.sku_id IS NOT NULL THEN
                    UPDATE skus SET reorder_level = NEW.reorder_level
                    WHERE id = NEW.sku_id AND reorder_level IS DISTINCT FROM NEW.reorder_level;
                END IF;
                RETURN NEW;
            END;
            $$;

            DROP TRIGGER IF EXISTS trg_items_reorder_level_to_sku ON items;
            CREATE TRIGGER trg_items_reorder_level_to_sku
                AFTER INSERT OR UPDATE OF reorder_level ON items
                FOR EACH ROW EXECUTE FUNCTION items_reorder_level_to_sku();

            UPDATE skus s SET reorder_level = i.reorder_level
            FROM items i WHERE i.sku_id = s.id AND s.reorder_level IS DISTINCT FROM i.reorder_level;

            -- Keep the newest open alert per item and type before enforcing uniqueness (the table has been empty so far).
            UPDATE inventory_alerts a SET status = 'RESOLVED', resolved_at = now(), resolution_note = 'تنبيه مكرر'
            WHERE a.status <> 'RESOLVED' AND EXISTS (
                SELECT 1 FROM inventory_alerts b
                WHERE b.item_id = a.item_id AND b.alert_type = a.alert_type AND b.status <> 'RESOLVED' AND b.created_at > a.created_at);

            CREATE UNIQUE INDEX IF NOT EXISTS ux_inventory_alerts_open_per_item_type
                ON inventory_alerts (item_id, alert_type) WHERE status <> 'RESOLVED';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS ux_inventory_alerts_open_per_item_type;
            DROP TRIGGER IF EXISTS trg_items_reorder_level_to_sku ON items;
            DROP FUNCTION IF EXISTS items_reorder_level_to_sku();
            """);
    }
}
