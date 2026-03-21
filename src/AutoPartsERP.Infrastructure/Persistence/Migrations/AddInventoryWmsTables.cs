using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20240101000004_AddInventoryWmsTables")]
public sealed class AddInventoryWmsTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"uuid-ossp\";");
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"ltree\";");
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"pg_trgm\";");

        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION normalize_part_number(raw TEXT)
            RETURNS TEXT LANGUAGE sql IMMUTABLE AS $$
              SELECT upper(regexp_replace(coalesce(raw, ''), '[^A-Za-z0-9]', '', 'g'))
            $$;
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS items (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                sku_id uuid NULL REFERENCES skus(id),
                part_number text NOT NULL,
                part_number_canonical text GENERATED ALWAYS AS (normalize_part_number(part_number)) STORED,
                part_number_numeric text GENERATED ALWAYS AS (regexp_replace(part_number, '[^0-9]', '', 'g')) STORED,
                name_en text NOT NULL,
                name_ar text NOT NULL,
                name_ar_colloquial text NULL,
                brand text NULL,
                category_path ltree NULL,
                unit_of_measure text NOT NULL DEFAULT 'PIECE',
                has_warranty boolean NOT NULL DEFAULT false,
                warranty_months int NOT NULL DEFAULT 0,
                is_batch_tracked boolean NOT NULL DEFAULT false,
                reorder_level numeric(18,4) NOT NULL DEFAULT 0,
                is_active boolean NOT NULL DEFAULT true,
                is_stop_ship boolean NOT NULL DEFAULT false,
                stop_ship_reason text NULL,
                notes text NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL,
                updated_at timestamptz NULL,
                updated_by uuid NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS idx_items_canonical ON items(part_number_canonical);
            CREATE INDEX IF NOT EXISTS idx_items_numeric ON items(part_number_numeric);
            CREATE INDEX IF NOT EXISTS idx_items_sku ON items(sku_id);
            CREATE INDEX IF NOT EXISTS idx_items_trgm_canonical ON items USING gin(part_number_canonical gin_trgm_ops);
            CREATE INDEX IF NOT EXISTS idx_items_trgm_name_ar ON items USING gin(name_ar gin_trgm_ops);
            CREATE INDEX IF NOT EXISTS idx_items_trgm_colloquial ON items USING gin(name_ar_colloquial gin_trgm_ops);
            CREATE INDEX IF NOT EXISTS idx_items_category ON items USING gist(category_path);
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS item_aliases (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                item_id uuid NOT NULL REFERENCES items(id),
                alias text NOT NULL,
                alias_canonical text GENERATED ALWAYS AS (normalize_part_number(alias)) STORED,
                source text NOT NULL DEFAULT 'MANUAL',
                created_at timestamptz NOT NULL DEFAULT now(),
                UNIQUE (item_id, alias_canonical)
            );
            CREATE INDEX IF NOT EXISTS idx_item_aliases_alias_trgm ON item_aliases USING gin(alias_canonical gin_trgm_ops);
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS item_interchanges (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                item_id uuid NOT NULL REFERENCES items(id),
                interchange_item_id uuid NOT NULL REFERENCES items(id),
                type text NOT NULL,
                priority int NOT NULL DEFAULT 1,
                notes text NULL,
                is_active boolean NOT NULL DEFAULT true,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL,
                UNIQUE (item_id, interchange_item_id),
                CHECK (item_id <> interchange_item_id)
            );
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS item_reorder_settings (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                item_id uuid NOT NULL REFERENCES items(id),
                warehouse_id uuid NOT NULL REFERENCES locations(id),
                reorder_point numeric(18,4) NOT NULL,
                reorder_qty numeric(18,4) NOT NULL,
                max_stock numeric(18,4) NULL,
                is_active boolean NOT NULL DEFAULT true,
                UNIQUE (item_id, warehouse_id)
            );
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS inventory_statuses (
                code text PRIMARY KEY,
                label text NOT NULL,
                label_ar text NOT NULL,
                allows_sale boolean NOT NULL DEFAULT false
            );

            INSERT INTO inventory_statuses(code,label,label_ar,allows_sale) VALUES
                ('AVAILABLE','Available','متاح',true),
                ('QC_HOLD','QC Hold','حجر الجودة',false),
                ('IN_TRANSIT','In Transit','في الطريق',false),
                ('STOP_SHIP','Stop Ship','إيقاف شحن',false),
                ('RECEIVING','Receiving','استلام',false),
                ('RESERVED','Reserved','محجوز',false)
            ON CONFLICT(code) DO NOTHING;
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS inventory_balances (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                item_id uuid NOT NULL REFERENCES items(id),
                location_id uuid NOT NULL REFERENCES locations(id),
                batch_id uuid NULL REFERENCES batches(id),
                status text NOT NULL REFERENCES inventory_statuses(code),
                qty numeric(18,4) NOT NULL DEFAULT 0,
                updated_at timestamptz NOT NULL DEFAULT now(),
                UNIQUE (item_id, location_id, batch_id, status),
                CHECK (qty >= 0)
            );
            CREATE INDEX IF NOT EXISTS idx_inventory_balances_item_status ON inventory_balances(item_id, status);
            CREATE INDEX IF NOT EXISTS idx_inventory_balances_location ON inventory_balances(location_id);
            CREATE INDEX IF NOT EXISTS idx_inventory_balances_low_stock ON inventory_balances(item_id)
                WHERE status = 'AVAILABLE' AND qty = 0;
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS inventory_movements (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                item_id uuid NOT NULL REFERENCES items(id),
                location_id uuid NOT NULL REFERENCES locations(id),
                batch_id uuid NULL REFERENCES batches(id),
                movement_type text NOT NULL,
                qty numeric(18,4) NOT NULL,
                direction text NOT NULL CHECK (direction IN ('IN','OUT')),
                from_status text NULL REFERENCES inventory_statuses(code),
                to_status text NULL REFERENCES inventory_statuses(code),
                reference_type text NULL,
                reference_id uuid NULL,
                performed_by uuid NOT NULL REFERENCES asp_net_users(id),
                correlation_id uuid NOT NULL,
                notes text NULL,
                created_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS idx_inventory_movements_item_time ON inventory_movements(item_id, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_inventory_movements_ref ON inventory_movements(reference_type, reference_id);
            """);

        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS trg_inventory_movements_immutable ON inventory_movements;
            CREATE TRIGGER trg_inventory_movements_immutable
                BEFORE UPDATE OR DELETE ON inventory_movements
                FOR EACH ROW EXECUTE FUNCTION fn_audit_log_immutable();
            """);

        migrationBuilder.Sql("""
            CREATE SEQUENCE IF NOT EXISTS receiving_document_seq START 1;
            CREATE TABLE IF NOT EXISTS receiving_documents (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                document_no text NOT NULL UNIQUE,
                vendor_party_id uuid NULL REFERENCES parties(id),
                purchase_order_ref text NULL,
                warehouse_id uuid NOT NULL REFERENCES locations(id),
                status text NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT','IN_PROGRESS','COMPLETED','CANCELLED')),
                received_by uuid NOT NULL REFERENCES asp_net_users(id),
                received_at timestamptz NULL,
                posted_at timestamptz NULL,
                notes text NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL
            );
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS receiving_lines (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                receiving_document_id uuid NOT NULL REFERENCES receiving_documents(id),
                item_id uuid NOT NULL REFERENCES items(id),
                expected_qty numeric(18,4) NULL,
                received_qty numeric(18,4) NOT NULL DEFAULT 0,
                rejected_qty numeric(18,4) NOT NULL DEFAULT 0,
                batch_id uuid NULL REFERENCES batches(id),
                assigned_location_id uuid NULL REFERENCES locations(id),
                condition_status text NOT NULL DEFAULT 'GOOD' CHECK (condition_status IN ('GOOD','DAMAGED','PARTIAL')),
                manufacturer_part_match_ok boolean NULL,
                notes text NULL,
                created_at timestamptz NOT NULL DEFAULT now()
            );
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS putaway_tasks (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                receiving_line_id uuid NOT NULL REFERENCES receiving_lines(id),
                from_location_id uuid NOT NULL REFERENCES locations(id),
                to_location_id uuid NOT NULL REFERENCES locations(id),
                qty numeric(18,4) NOT NULL,
                status text NOT NULL DEFAULT 'PENDING' CHECK (status IN ('PENDING','IN_PROGRESS','COMPLETED')),
                assigned_to uuid NULL REFERENCES asp_net_users(id),
                confirmed_by uuid NULL REFERENCES asp_net_users(id),
                confirmed_at timestamptz NULL,
                created_at timestamptz NOT NULL DEFAULT now()
            );
            """);

        migrationBuilder.Sql("""
            CREATE SEQUENCE IF NOT EXISTS issue_order_seq START 1;
            CREATE TABLE IF NOT EXISTS issue_orders (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                order_no text NOT NULL UNIQUE,
                source_type text NOT NULL,
                source_id uuid NULL,
                warehouse_id uuid NOT NULL REFERENCES locations(id),
                status text NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT','PICKING','VERIFYING','ISSUED','CANCELLED')),
                created_by uuid NOT NULL REFERENCES asp_net_users(id),
                issued_at timestamptz NULL,
                created_at timestamptz NOT NULL DEFAULT now()
            );

            CREATE TABLE IF NOT EXISTS issue_order_lines (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                issue_order_id uuid NOT NULL REFERENCES issue_orders(id),
                item_id uuid NOT NULL REFERENCES items(id),
                requested_qty numeric(18,4) NOT NULL,
                picked_qty numeric(18,4) NOT NULL DEFAULT 0,
                verified_qty numeric(18,4) NOT NULL DEFAULT 0,
                issued_qty numeric(18,4) NOT NULL DEFAULT 0,
                source_location_id uuid NULL REFERENCES locations(id),
                batch_id uuid NULL REFERENCES batches(id)
            );

            CREATE TABLE IF NOT EXISTS pick_tasks (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                issue_order_line_id uuid NOT NULL REFERENCES issue_order_lines(id),
                location_id uuid NOT NULL REFERENCES locations(id),
                qty numeric(18,4) NOT NULL,
                pick_sequence int NOT NULL DEFAULT 0,
                status text NOT NULL DEFAULT 'PENDING' CHECK (status IN ('PENDING','PICKED','VERIFIED','CANCELLED')),
                assigned_to uuid NULL REFERENCES asp_net_users(id),
                picked_by uuid NULL REFERENCES asp_net_users(id),
                picked_at timestamptz NULL,
                verified_by uuid NULL REFERENCES asp_net_users(id),
                verified_at timestamptz NULL,
                created_at timestamptz NOT NULL DEFAULT now()
            );
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS transfer_requests (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                source_warehouse_id uuid NOT NULL REFERENCES locations(id),
                destination_warehouse_id uuid NOT NULL REFERENCES locations(id),
                status text NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT','PENDING_APPROVAL','APPROVED','REJECTED')),
                requested_by uuid NOT NULL REFERENCES asp_net_users(id),
                approved_by uuid NULL REFERENCES asp_net_users(id),
                approval_id uuid NULL REFERENCES approval_requests(id),
                notes text NULL,
                created_at timestamptz NOT NULL DEFAULT now()
            );

            CREATE TABLE IF NOT EXISTS transfer_request_lines (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                transfer_request_id uuid NOT NULL REFERENCES transfer_requests(id),
                item_id uuid NOT NULL REFERENCES items(id),
                requested_qty numeric(18,4) NOT NULL,
                approved_qty numeric(18,4) NULL
            );
            """);

        migrationBuilder.Sql("""
            CREATE SEQUENCE IF NOT EXISTS transfer_order_seq START 1;
            CREATE TABLE IF NOT EXISTS transfer_orders (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                transfer_no text NOT NULL UNIQUE,
                transfer_request_id uuid NULL REFERENCES transfer_requests(id),
                source_warehouse_id uuid NOT NULL REFERENCES locations(id),
                destination_warehouse_id uuid NOT NULL REFERENCES locations(id),
                internal_tracking_no text NULL,
                status text NOT NULL DEFAULT 'DRAFT'
                    CHECK (status IN ('DRAFT','PICKING','SHIPPED','IN_TRANSIT','PARTIALLY_RECEIVED','RECEIVED','CANCELLED')),
                shipped_at timestamptz NULL,
                received_at timestamptz NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL
            );

            CREATE TABLE IF NOT EXISTS transfer_order_lines (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                transfer_order_id uuid NOT NULL REFERENCES transfer_orders(id),
                item_id uuid NOT NULL REFERENCES items(id),
                batch_id uuid NULL REFERENCES batches(id),
                source_location_id uuid NULL REFERENCES locations(id),
                destination_location_id uuid NULL REFERENCES locations(id),
                shipped_qty numeric(18,4) NOT NULL DEFAULT 0,
                received_qty numeric(18,4) NOT NULL DEFAULT 0
            );
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS cycle_count_plans (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                warehouse_id uuid NOT NULL REFERENCES locations(id),
                scope_type text NOT NULL,
                scope_filter jsonb NULL,
                status text NOT NULL DEFAULT 'DRAFT'
                    CHECK (status IN ('DRAFT','IN_PROGRESS','PENDING_APPROVAL','POSTED','CANCELLED')),
                scheduled_for date NOT NULL,
                created_by uuid NOT NULL REFERENCES asp_net_users(id),
                posted_by uuid NULL REFERENCES asp_net_users(id),
                posted_at timestamptz NULL,
                created_at timestamptz NOT NULL DEFAULT now()
            );

            CREATE TABLE IF NOT EXISTS cycle_count_lines (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                cycle_count_plan_id uuid NOT NULL REFERENCES cycle_count_plans(id),
                item_id uuid NOT NULL REFERENCES items(id),
                location_id uuid NOT NULL REFERENCES locations(id),
                system_qty numeric(18,4) NOT NULL DEFAULT 0,
                counted_qty numeric(18,4) NULL,
                variance_qty numeric(18,4) NOT NULL DEFAULT 0,
                reason_code text NULL,
                notes text NULL
            );
            """);

        migrationBuilder.Sql("""
            CREATE SEQUENCE IF NOT EXISTS stock_adjustment_seq START 1;
            CREATE TABLE IF NOT EXISTS stock_adjustments (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                adjustment_no text NOT NULL UNIQUE,
                adjustment_type text NOT NULL CHECK (adjustment_type IN ('CYCLE_COUNT','DAMAGE','FOUND','MANUAL')),
                warehouse_id uuid NOT NULL REFERENCES locations(id),
                reason_code text NOT NULL,
                status text NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT','POSTED','CANCELLED')),
                posted_at timestamptz NULL,
                posted_by uuid NULL REFERENCES asp_net_users(id),
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL
            );

            CREATE TABLE IF NOT EXISTS stock_adjustment_lines (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                stock_adjustment_id uuid NOT NULL REFERENCES stock_adjustments(id),
                item_id uuid NOT NULL REFERENCES items(id),
                location_id uuid NOT NULL REFERENCES locations(id),
                batch_id uuid NULL REFERENCES batches(id),
                status text NOT NULL REFERENCES inventory_statuses(code),
                qty_delta numeric(18,4) NOT NULL,
                system_qty_before numeric(18,4) NOT NULL,
                system_qty_after numeric(18,4) NOT NULL,
                notes text NULL
            );
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS inventory_alerts (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                item_id uuid NOT NULL REFERENCES items(id),
                alert_type text NOT NULL,
                severity text NOT NULL,
                message text NOT NULL,
                threshold_value numeric(18,4) NULL,
                current_value numeric(18,4) NULL,
                status text NOT NULL DEFAULT 'OPEN' CHECK (status IN ('OPEN','ACKNOWLEDGED','RESOLVED')),
                acknowledged_by uuid NULL REFERENCES asp_net_users(id),
                acknowledged_at timestamptz NULL,
                resolved_by uuid NULL REFERENCES asp_net_users(id),
                resolved_at timestamptz NULL,
                resolution_note text NULL,
                created_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS idx_inventory_alerts_status ON inventory_alerts(status);
            CREATE INDEX IF NOT EXISTS idx_inventory_alerts_item_id ON inventory_alerts(item_id);
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS barcode_scan_logs (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                scan_code text NOT NULL,
                scan_type text NOT NULL,
                item_id uuid NULL REFERENCES items(id),
                batch_id uuid NULL REFERENCES batches(id),
                location_id uuid NULL REFERENCES locations(id),
                scanned_by uuid NOT NULL REFERENCES asp_net_users(id),
                scanned_at timestamptz NOT NULL DEFAULT now(),
                device_id text NULL
            );
            CREATE INDEX IF NOT EXISTS idx_barcode_scan_logs_time ON barcode_scan_logs(scan_code, scanned_at DESC);
            """);

        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION fn_transfer_order_sync_status_balances()
            RETURNS trigger AS
            $$
            BEGIN
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            DROP TRIGGER IF EXISTS trg_transfer_orders_sync_balances ON transfer_orders;
            CREATE TRIGGER trg_transfer_orders_sync_balances
                AFTER UPDATE OF status ON transfer_orders
                FOR EACH ROW EXECUTE FUNCTION fn_transfer_order_sync_status_balances();
            """);

        foreach (var permission in PermissionCodes.All)
        {
            var safePermission = permission.Replace("'", "''", StringComparison.Ordinal);
            migrationBuilder.Sql($"""
                INSERT INTO asp_net_role_claims (role_id,claim_type,claim_value)
                SELECT '10000000-0000-0000-0000-000000000001','permission','{safePermission}'
                WHERE NOT EXISTS (
                    SELECT 1 FROM asp_net_role_claims
                    WHERE role_id = '10000000-0000-0000-0000-000000000001'
                      AND claim_type = 'permission'
                      AND claim_value = '{safePermission}');
                """);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
