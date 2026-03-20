using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

public sealed class AddOperationalCoreTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"uuid-ossp\";");
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"ltree\";");
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"pg_trgm\";");

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS customers (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                code text NOT NULL UNIQUE,
                name text NOT NULL,
                type text NOT NULL CHECK (type IN ('WORKSHOP','RETAIL','WHOLESALE')),
                phone text NULL,
                phone2 text NULL,
                address text NULL,
                city text NULL,
                region text NULL,
                tax_number text NULL,
                credit_limit_syp numeric(18,4) NOT NULL DEFAULT 0,
                credit_limit_usd numeric(18,4) NOT NULL DEFAULT 0,
                payment_terms_days int NOT NULL DEFAULT 30,
                is_active boolean NOT NULL DEFAULT true,
                assigned_sales_rep uuid NULL,
                notes text NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL,
                updated_at timestamptz NULL,
                updated_by uuid NULL
            );
            CREATE INDEX IF NOT EXISTS ix_customers_code ON customers(code);
            CREATE INDEX IF NOT EXISTS ix_customers_type_is_active ON customers(type, is_active);
            CREATE INDEX IF NOT EXISTS ix_customers_name_trgm ON customers USING GIN (name gin_trgm_ops);
            CREATE INDEX IF NOT EXISTS ix_customers_assigned_sales_rep ON customers(assigned_sales_rep);
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS fx_rates (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                rate_date date NOT NULL,
                currency_from text NOT NULL DEFAULT 'USD',
                currency_to text NOT NULL DEFAULT 'SYP',
                buy_rate numeric(18,4) NOT NULL,
                sell_rate numeric(18,4) NOT NULL,
                mid_rate numeric(18,4) GENERATED ALWAYS AS ((buy_rate + sell_rate) / 2) STORED,
                is_active boolean NOT NULL DEFAULT true,
                notes text NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL,
                CONSTRAINT uq_fx_rates UNIQUE (rate_date, currency_from, currency_to)
            );
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS categories (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                path ltree NOT NULL UNIQUE,
                name text NOT NULL,
                name_ar text NULL,
                parent_id uuid NULL REFERENCES categories(id),
                depth int NOT NULL DEFAULT 0,
                is_active boolean NOT NULL DEFAULT true,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_categories_path_gist ON categories USING GIST(path);
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS attribute_schemas (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                category_id uuid NOT NULL REFERENCES categories(id),
                code text NOT NULL,
                label text NOT NULL,
                label_ar text NULL,
                data_type text NOT NULL CHECK (data_type IN ('TEXT','NUMBER','BOOLEAN','SELECT')),
                is_required boolean NOT NULL DEFAULT false,
                is_filterable boolean NOT NULL DEFAULT false,
                sort_order int NOT NULL DEFAULT 0,
                options jsonb NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT uq_attribute_schemas UNIQUE (category_id, code)
            );
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS skus (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                code text NOT NULL UNIQUE,
                name text NOT NULL,
                name_ar text NOT NULL,
                category_id uuid NOT NULL REFERENCES categories(id),
                barcode text NULL,
                unit_of_measure text NOT NULL DEFAULT 'PIECE',
                cost_price_syp numeric(18,4) NOT NULL DEFAULT 0,
                cost_price_usd numeric(18,4) NOT NULL DEFAULT 0,
                selling_price_syp numeric(18,4) NOT NULL DEFAULT 0,
                selling_price_usd numeric(18,4) NOT NULL DEFAULT 0,
                min_selling_price_syp numeric(18,4) NOT NULL DEFAULT 0,
                min_selling_price_usd numeric(18,4) NOT NULL DEFAULT 0,
                is_batch_tracked boolean NOT NULL DEFAULT false,
                has_warranty boolean NOT NULL DEFAULT false,
                warranty_months int NOT NULL DEFAULT 0,
                reorder_level numeric(18,4) NOT NULL DEFAULT 0,
                is_active boolean NOT NULL DEFAULT true,
                notes text NULL,
                attributes jsonb NOT NULL DEFAULT '{}'::jsonb,
                tags text[] NOT NULL DEFAULT '{}'::text[],
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL,
                updated_at timestamptz NULL,
                updated_by uuid NULL
            );
            CREATE INDEX IF NOT EXISTS ix_skus_category_id ON skus(category_id);
            CREATE UNIQUE INDEX IF NOT EXISTS ix_skus_barcode_not_null ON skus(barcode) WHERE barcode IS NOT NULL;
            CREATE INDEX IF NOT EXISTS ix_skus_name_trgm ON skus USING GIN (name gin_trgm_ops);
            CREATE INDEX IF NOT EXISTS ix_skus_name_ar_trgm ON skus USING GIN (name_ar gin_trgm_ops);
            CREATE INDEX IF NOT EXISTS ix_skus_tags_gin ON skus USING GIN (tags);
            CREATE INDEX IF NOT EXISTS ix_skus_attributes_gin ON skus USING GIN (attributes jsonb_path_ops);
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS locations (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                code text NOT NULL UNIQUE,
                name text NOT NULL,
                type text NOT NULL CHECK (type IN ('WAREHOUSE','SHELF','VEHICLE','RETURN','QUARANTINE')),
                parent_id uuid NULL REFERENCES locations(id),
                is_active boolean NOT NULL DEFAULT true,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL
            );
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS inventory_stock (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                sku_id uuid NOT NULL REFERENCES skus(id),
                location_id uuid NOT NULL REFERENCES locations(id),
                quantity_on_hand numeric(18,4) NOT NULL DEFAULT 0,
                quantity_reserved numeric(18,4) NOT NULL DEFAULT 0,
                quantity_available numeric(18,4) GENERATED ALWAYS AS (quantity_on_hand - quantity_reserved) STORED,
                updated_at timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT uq_inventory_stock UNIQUE (sku_id, location_id),
                CONSTRAINT ck_inventory_stock_on_hand_non_negative CHECK (quantity_on_hand >= 0),
                CONSTRAINT ck_inventory_stock_reserved_non_negative CHECK (quantity_reserved >= 0),
                CONSTRAINT ck_inventory_stock_reserved_lte_on_hand CHECK (quantity_reserved <= quantity_on_hand)
            );
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS batches (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                batch_number text NOT NULL UNIQUE,
                sku_id uuid NOT NULL REFERENCES skus(id),
                location_id uuid NOT NULL REFERENCES locations(id),
                quantity_initial numeric(18,4) NOT NULL,
                quantity_current numeric(18,4) NOT NULL,
                cost_price_syp numeric(18,4) NOT NULL,
                cost_price_usd numeric(18,4) NOT NULL,
                fx_rate_id uuid NOT NULL REFERENCES fx_rates(id),
                supplier_name text NULL,
                supplier_invoice text NULL,
                received_date date NOT NULL,
                expiry_date date NULL,
                status text NOT NULL DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE','DEPLETED','QUARANTINE','RETURNED')),
                notes text NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL,
                CONSTRAINT ck_batches_quantity_non_negative CHECK (quantity_current >= 0)
            );
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS batch_movements (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                batch_id uuid NOT NULL REFERENCES batches(id),
                movement_type text NOT NULL CHECK (movement_type IN ('RECEIPT','INVOICE_OUT','RETURN_IN','TRANSFER_OUT','TRANSFER_IN','ADJUSTMENT','WARRANTY_OUT','WARRANTY_IN','WRITE_OFF')),
                quantity numeric(18,4) NOT NULL,
                direction text NOT NULL CHECK (direction IN ('IN','OUT')),
                reference_type text NULL,
                reference_id uuid NULL,
                from_location_id uuid NULL REFERENCES locations(id),
                to_location_id uuid NULL REFERENCES locations(id),
                unit_cost_syp numeric(18,4) NOT NULL DEFAULT 0,
                unit_cost_usd numeric(18,4) NOT NULL DEFAULT 0,
                performed_by uuid NULL,
                notes text NULL,
                created_at timestamptz NOT NULL DEFAULT now()
            );
            """);

        migrationBuilder.Sql("""
            CREATE SEQUENCE IF NOT EXISTS invoice_number_seq START 1;
            CREATE TABLE IF NOT EXISTS invoices (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                invoice_number text UNIQUE,
                invoice_type text NOT NULL CHECK (invoice_type IN ('SALE','RETURN','CREDIT_NOTE')),
                status text NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT','CONFIRMED','POSTED','VOID','CANCELLED')),
                customer_id uuid NOT NULL REFERENCES customers(id),
                invoice_date date NOT NULL,
                due_date date NOT NULL,
                delivery_address text NULL,
                subtotal_syp numeric(18,4) NOT NULL DEFAULT 0,
                subtotal_usd numeric(18,4) NOT NULL DEFAULT 0,
                discount_amount_syp numeric(18,4) NOT NULL DEFAULT 0,
                discount_amount_usd numeric(18,4) NOT NULL DEFAULT 0,
                delivery_fee_syp numeric(18,4) NOT NULL DEFAULT 0,
                delivery_fee_usd numeric(18,4) NOT NULL DEFAULT 0,
                tax_amount_syp numeric(18,4) NOT NULL DEFAULT 0,
                tax_amount_usd numeric(18,4) NOT NULL DEFAULT 0,
                total_syp numeric(18,4) NOT NULL DEFAULT 0,
                total_usd numeric(18,4) NOT NULL DEFAULT 0,
                paid_syp numeric(18,4) NOT NULL DEFAULT 0,
                paid_usd numeric(18,4) NOT NULL DEFAULT 0,
                balance_syp numeric(18,4) GENERATED ALWAYS AS (total_syp - paid_syp) STORED,
                balance_usd numeric(18,4) GENERATED ALWAYS AS (total_usd - paid_usd) STORED,
                fx_rate_id uuid NOT NULL REFERENCES fx_rates(id),
                fx_rate_snapshot numeric(18,4) NOT NULL,
                original_invoice_id uuid NULL REFERENCES invoices(id),
                sales_rep_id uuid NULL,
                reason_code text NULL,
                notes text NULL,
                posted_at timestamptz NULL,
                posted_by uuid NULL,
                voided_at timestamptz NULL,
                voided_by uuid NULL,
                void_reason text NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL,
                updated_at timestamptz NULL,
                updated_by uuid NULL
            );
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS invoice_lines (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                invoice_id uuid NOT NULL REFERENCES invoices(id) ON DELETE RESTRICT,
                line_number int NOT NULL,
                sku_id uuid NOT NULL REFERENCES skus(id),
                batch_id uuid NULL REFERENCES batches(id),
                location_id uuid NOT NULL REFERENCES locations(id),
                description text NULL,
                quantity numeric(18,4) NOT NULL,
                unit_price_syp numeric(18,4) NOT NULL,
                unit_price_usd numeric(18,4) NOT NULL,
                discount_pct numeric(5,2) NOT NULL DEFAULT 0 CHECK (discount_pct BETWEEN 0 AND 100),
                line_total_syp numeric(18,4) GENERATED ALWAYS AS (quantity * unit_price_syp * (1 - discount_pct / 100)) STORED,
                line_total_usd numeric(18,4) GENERATED ALWAYS AS (quantity * unit_price_usd * (1 - discount_pct / 100)) STORED,
                cost_price_syp numeric(18,4) NOT NULL DEFAULT 0,
                cost_price_usd numeric(18,4) NOT NULL DEFAULT 0,
                fx_rate_used numeric(18,4) NOT NULL DEFAULT 0,
                is_price_override boolean NOT NULL DEFAULT false,
                price_override_reason text NULL,
                price_override_approved_by uuid NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT uq_invoice_lines UNIQUE (invoice_id, line_number),
                CONSTRAINT ck_invoice_lines_quantity_positive CHECK (quantity > 0),
                CONSTRAINT ck_invoice_lines_unit_price_syp_non_negative CHECK (unit_price_syp >= 0)
            );
            """);

        migrationBuilder.Sql("""
            CREATE SEQUENCE IF NOT EXISTS payment_number_seq START 1;
            CREATE TABLE IF NOT EXISTS payments (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                payment_number text UNIQUE,
                payment_type text NOT NULL CHECK (payment_type IN ('RECEIPT','REFUND')),
                customer_id uuid NOT NULL REFERENCES customers(id),
                payment_date date NOT NULL,
                payment_method text NOT NULL CHECK (payment_method IN ('CASH','BANK_TRANSFER','CHEQUE','USD_CASH')),
                amount_syp numeric(18,4) NOT NULL DEFAULT 0,
                amount_usd numeric(18,4) NOT NULL DEFAULT 0,
                allocated_syp numeric(18,4) NOT NULL DEFAULT 0,
                allocated_usd numeric(18,4) NOT NULL DEFAULT 0,
                unallocated_syp numeric(18,4) GENERATED ALWAYS AS (amount_syp - allocated_syp) STORED,
                unallocated_usd numeric(18,4) GENERATED ALWAYS AS (amount_usd - allocated_usd) STORED,
                fx_rate_id uuid NOT NULL REFERENCES fx_rates(id),
                reference_number text NULL,
                bank_name text NULL,
                cheque_number text NULL,
                cheque_date date NULL,
                notes text NULL,
                is_reversed boolean NOT NULL DEFAULT false,
                reversed_at timestamptz NULL,
                reversed_by uuid NULL,
                reversal_reason text NULL,
                received_by uuid NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL,
                CONSTRAINT ck_payments_non_negative CHECK (amount_syp >= 0 AND amount_usd >= 0),
                CONSTRAINT ck_payments_positive CHECK (amount_syp > 0 OR amount_usd > 0)
            );
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS payment_allocations (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                payment_id uuid NOT NULL REFERENCES payments(id),
                invoice_id uuid NOT NULL REFERENCES invoices(id),
                allocated_syp numeric(18,4) NOT NULL DEFAULT 0,
                allocated_usd numeric(18,4) NOT NULL DEFAULT 0,
                allocation_date date NOT NULL,
                notes text NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL,
                CONSTRAINT uq_payment_allocations UNIQUE (payment_id, invoice_id),
                CONSTRAINT ck_payment_allocations_non_negative CHECK (allocated_syp >= 0 AND allocated_usd >= 0),
                CONSTRAINT ck_payment_allocations_positive CHECK (allocated_syp > 0 OR allocated_usd > 0)
            );
            """);

        migrationBuilder.Sql("""
            CREATE SEQUENCE IF NOT EXISTS warranty_number_seq START 1;
            CREATE TABLE IF NOT EXISTS warranty_records (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                warranty_number text NOT NULL UNIQUE,
                invoice_id uuid NULL REFERENCES invoices(id),
                invoice_line_id uuid NOT NULL REFERENCES invoice_lines(id),
                sku_id uuid NOT NULL REFERENCES skus(id),
                batch_id uuid NULL REFERENCES batches(id),
                customer_id uuid NOT NULL REFERENCES customers(id),
                sale_date date NOT NULL,
                expiry_date date NOT NULL,
                claim_date date NULL,
                status text NOT NULL DEFAULT 'ACTIVE' CHECK (status IN ('ACTIVE','CLAIMED','EXPIRED','REJECTED','VOIDED')),
                claim_description text NULL,
                resolution text NULL,
                replacement_sku_id uuid NULL REFERENCES skus(id),
                replacement_batch_id uuid NULL REFERENCES batches(id),
                replacement_invoice_id uuid NULL REFERENCES invoices(id),
                processed_by uuid NULL,
                processed_at timestamptz NULL,
                rejection_reason text NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL,
                updated_at timestamptz NULL,
                updated_by uuid NULL
            );
            """);

        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION fn_audit_log_immutable()
            RETURNS trigger AS
            $$
            BEGIN
                RAISE EXCEPTION 'Append-only table: % cannot be modified', TG_TABLE_NAME;
            END;
            $$ LANGUAGE plpgsql;
            """);

        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS trg_batch_movements_immutable ON batch_movements;
            CREATE TRIGGER trg_batch_movements_immutable
            BEFORE UPDATE OR DELETE ON batch_movements
            FOR EACH ROW EXECUTE FUNCTION fn_audit_log_immutable();
            """);

        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS trg_payment_allocations_immutable ON payment_allocations;
            CREATE TRIGGER trg_payment_allocations_immutable
            BEFORE UPDATE OR DELETE ON payment_allocations
            FOR EACH ROW EXECUTE FUNCTION fn_audit_log_immutable();
            """);

        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION fn_invoice_auto_number()
            RETURNS trigger AS
            $$
            BEGIN
                IF NEW.invoice_number IS NULL OR btrim(NEW.invoice_number) = '' THEN
                    NEW.invoice_number := 'INV-' || to_char(COALESCE(NEW.invoice_date, CURRENT_DATE), 'YYYY')
                        || '-' || lpad(nextval('invoice_number_seq')::text, 5, '0');
                END IF;
                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            DROP TRIGGER IF EXISTS trg_invoice_auto_number ON invoices;
            CREATE TRIGGER trg_invoice_auto_number
            BEFORE INSERT ON invoices
            FOR EACH ROW EXECUTE FUNCTION fn_invoice_auto_number();
            """);

        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION fn_invoice_posted_immutable()
            RETURNS trigger AS
            $$
            BEGIN
                IF OLD.status IN ('VOID', 'CANCELLED') THEN
                    RAISE EXCEPTION 'VOID/CANCELLED invoices are immutable';
                END IF;

                IF OLD.status = 'POSTED' THEN
                    IF NEW.customer_id IS DISTINCT FROM OLD.customer_id
                       OR NEW.invoice_date IS DISTINCT FROM OLD.invoice_date
                       OR NEW.total_syp IS DISTINCT FROM OLD.total_syp
                       OR NEW.total_usd IS DISTINCT FROM OLD.total_usd
                       OR NEW.fx_rate_id IS DISTINCT FROM OLD.fx_rate_id THEN
                        RAISE EXCEPTION 'POSTED invoice core fields are immutable';
                    END IF;
                END IF;

                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;
            DROP TRIGGER IF EXISTS trg_invoice_posted_immutable ON invoices;
            CREATE TRIGGER trg_invoice_posted_immutable
            BEFORE UPDATE ON invoices
            FOR EACH ROW EXECUTE FUNCTION fn_invoice_posted_immutable();
            """);

        migrationBuilder.Sql("""
            CREATE MATERIALIZED VIEW IF NOT EXISTS customer_account_summary AS
            SELECT
                c.id AS customer_id,
                COALESCE(SUM(i.total_syp), 0) AS total_invoiced_syp,
                COALESCE(SUM(i.total_usd), 0) AS total_invoiced_usd,
                COALESCE(SUM(i.paid_syp), 0) AS total_paid_syp,
                COALESCE(SUM(i.paid_usd), 0) AS total_paid_usd,
                COALESCE(SUM(i.balance_syp), 0) AS outstanding_syp,
                COALESCE(SUM(i.balance_usd), 0) AS outstanding_usd
            FROM customers c
            LEFT JOIN invoices i ON i.customer_id = c.id
            GROUP BY c.id;
            CREATE UNIQUE INDEX IF NOT EXISTS ux_customer_account_summary_customer_id
                ON customer_account_summary(customer_id);
            """);

        migrationBuilder.Sql("""
            CREATE MATERIALIZED VIEW IF NOT EXISTS sku_stock_summary AS
            SELECT
                s.id AS sku_id,
                COALESCE(SUM(st.quantity_on_hand), 0) AS total_on_hand,
                COALESCE(SUM(st.quantity_reserved), 0) AS total_reserved,
                COALESCE(SUM(st.quantity_available), 0) AS total_available,
                CASE
                    WHEN COALESCE(SUM(st.quantity_available), 0) <= s.reorder_level THEN true
                    ELSE false
                END AS low_stock_flag
            FROM skus s
            LEFT JOIN inventory_stock st ON st.sku_id = s.id
            GROUP BY s.id, s.reorder_level;
            CREATE UNIQUE INDEX IF NOT EXISTS ux_sku_stock_summary_sku_id
                ON sku_stock_summary(sku_id);
            """);

        migrationBuilder.Sql("""
            CREATE MATERIALIZED VIEW IF NOT EXISTS monthly_pl_summary AS
            SELECT
                EXTRACT(YEAR FROM i.invoice_date)::int AS year,
                EXTRACT(MONTH FROM i.invoice_date)::int AS month,
                COALESCE(SUM(il.line_total_syp), 0) AS total_revenue_syp,
                COALESCE(SUM(il.line_total_usd), 0) AS total_revenue_usd,
                COALESCE(SUM(il.quantity * il.cost_price_syp), 0) AS total_cogs_syp,
                COALESCE(SUM(il.quantity * il.cost_price_usd), 0) AS total_cogs_usd,
                COALESCE(SUM(il.line_total_syp - (il.quantity * il.cost_price_syp)), 0) AS gross_profit_syp,
                COALESCE(SUM(il.line_total_usd - (il.quantity * il.cost_price_usd)), 0) AS gross_profit_usd,
                CASE
                    WHEN COALESCE(SUM(il.line_total_syp), 0) = 0 THEN 0
                    ELSE ((COALESCE(SUM(il.line_total_syp - (il.quantity * il.cost_price_syp)), 0)
                        / COALESCE(SUM(il.line_total_syp), 1)) * 100)
                END AS gross_margin_pct
            FROM invoice_lines il
            INNER JOIN invoices i ON i.id = il.invoice_id
            WHERE i.status = 'POSTED'
            GROUP BY EXTRACT(YEAR FROM i.invoice_date), EXTRACT(MONTH FROM i.invoice_date);
            CREATE UNIQUE INDEX IF NOT EXISTS ux_monthly_pl_summary_year_month
                ON monthly_pl_summary(year, month);
            """);

        foreach (var permission in PermissionCodes.All)
        {
            var safePermission = permission.Replace("'", "''", StringComparison.Ordinal);
            migrationBuilder.Sql($"""
                INSERT INTO "AspNetRoleClaims" ("RoleId","ClaimType","ClaimValue")
                SELECT '10000000-0000-0000-0000-000000000001','permission','{safePermission}'
                WHERE NOT EXISTS (
                    SELECT 1 FROM "AspNetRoleClaims"
                    WHERE "RoleId" = '10000000-0000-0000-0000-000000000001'
                    AND "ClaimType" = 'permission'
                    AND "ClaimValue" = '{safePermission}');
                """);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
