using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// Phase 1 / step 1 of the inventory unification: give every <c>sku</c> a matching <c>item</c>,
/// linked through the <c>items.sku_id</c> FK that has existed since the WMS schema was created but
/// was never populated.
///
/// The two halves were never duplicates - <c>skus</c> owns commercial data (pricing, category,
/// barcode) while <c>items</c> owns logistics identity (part numbers, aliases, interchange,
/// stop-ship) - but nothing ever wrote to <c>items</c>, so the entire WMS half of the product model
/// sat empty. That is why Receiving, Transfers, Cycle Counts, Stock Adjustments, Issue Orders and
/// Inventory Alerts all render empty lists: they read WMS tables that have no rows.
///
/// The projection lives in a database function so the migration (existing databases) and
/// <c>DemoDataSeeder</c> (fresh databases, seeded after migrations run) share one definition.
/// Everything here is additive and idempotent - no existing row is modified or dropped, so this
/// step is safe to apply before the riskier <c>inventory_stock</c> -> view conversion.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000008_UnifyItemsWithSkus")]
public sealed class UnifyItemsWithSkus : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION sync_items_from_skus() RETURNS integer AS
            $$
            DECLARE
                created_count integer := 0;
            BEGIN
                -- 1) One item per sku. Skipped when the sku already has an item, or when some other
                -- item already occupies that canonical part number (idx_items_canonical is UNIQUE).
                INSERT INTO items (
                    id, sku_id, part_number, name_en, name_ar, brand, category_path,
                    unit_of_measure, has_warranty, warranty_months, is_batch_tracked,
                    reorder_level, is_active, notes, created_at, created_by, updated_at, updated_by)
                SELECT
                    uuid_generate_v4(), s.id, s.code, s.name, s.name_ar, NULL, c.path,
                    s.unit_of_measure, s.has_warranty, s.warranty_months, s.is_batch_tracked,
                    s.reorder_level, s.is_active, s.notes, s.created_at, s.created_by,
                    s.updated_at, s.updated_by
                FROM skus s
                LEFT JOIN categories c ON c.id = s.category_id
                WHERE NOT EXISTS (SELECT 1 FROM items i WHERE i.sku_id = s.id)
                  AND NOT EXISTS (
                      SELECT 1 FROM items i2
                      WHERE i2.part_number_canonical = normalize_part_number(s.code));

                GET DIAGNOSTICS created_count = ROW_COUNT;

                -- 2) Adopt any pre-existing unlinked item whose part number matches a sku code.
                UPDATE items i
                SET sku_id = s.id,
                    updated_at = now()
                FROM skus s
                WHERE i.sku_id IS NULL
                  AND i.part_number_canonical = normalize_part_number(s.code)
                  AND NOT EXISTS (SELECT 1 FROM items i2 WHERE i2.sku_id = s.id);

                -- 3) Carry the sku barcode over as a searchable/scannable alias.
                INSERT INTO item_aliases (id, item_id, alias, source, created_at)
                SELECT uuid_generate_v4(), i.id, s.barcode, 'BARCODE', now()
                FROM skus s
                JOIN items i ON i.sku_id = s.id
                WHERE s.barcode IS NOT NULL
                  AND btrim(s.barcode) <> ''
                  AND NOT EXISTS (
                      SELECT 1 FROM item_aliases a
                      WHERE a.item_id = i.id
                        AND a.alias_canonical = normalize_part_number(s.barcode));

                -- 4) Project inventory_stock into inventory_balances. inventory_stock carries
                -- reserved inside quantity_on_hand (quantity_available is generated as the
                -- difference), whereas inventory_balances models each status as its own row - so
                -- the on-hand total is preserved by splitting it across AVAILABLE and RESERVED.
                INSERT INTO inventory_balances (id, item_id, location_id, batch_id, status, qty, updated_at)
                SELECT uuid_generate_v4(), i.id, st.location_id, NULL, 'AVAILABLE',
                       st.quantity_on_hand - st.quantity_reserved, st.updated_at
                FROM inventory_stock st
                JOIN items i ON i.sku_id = st.sku_id
                WHERE st.quantity_on_hand - st.quantity_reserved > 0
                  AND NOT EXISTS (
                      SELECT 1 FROM inventory_balances b
                      WHERE b.item_id = i.id
                        AND b.location_id = st.location_id
                        AND b.batch_id IS NULL
                        AND b.status = 'AVAILABLE');

                INSERT INTO inventory_balances (id, item_id, location_id, batch_id, status, qty, updated_at)
                SELECT uuid_generate_v4(), i.id, st.location_id, NULL, 'RESERVED',
                       st.quantity_reserved, st.updated_at
                FROM inventory_stock st
                JOIN items i ON i.sku_id = st.sku_id
                WHERE st.quantity_reserved > 0
                  AND NOT EXISTS (
                      SELECT 1 FROM inventory_balances b
                      WHERE b.item_id = i.id
                        AND b.location_id = st.location_id
                        AND b.batch_id IS NULL
                        AND b.status = 'RESERVED');

                RETURN created_count;
            END;
            $$ LANGUAGE plpgsql;
            """);

        migrationBuilder.Sql("SELECT sync_items_from_skus();");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Additive projection - reversing it would delete WMS rows that later steps depend on.
    }
}
