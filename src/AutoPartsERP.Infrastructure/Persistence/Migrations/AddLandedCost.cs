using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// Landed costs (قيد رسملة مصاريف الشراء), after ERPNext's Landed Cost Voucher:
/// <list type="bullet">
/// <item><c>landed_cost_vouchers</c> (series LCV): the purchase bills whose goods carry the costs (<c>landed_cost_voucher_bills</c>) and
/// the charges — transport, customs, shipping, insurance, other — each split by value, quantity or equally, and each either owed to a
/// supplier or paid from a cash/bank account (<c>landed_cost_charges</c>).</item>
/// <item>Posting records how every charge was shared over the bill lines (<c>landed_cost_allocations</c>) and what it did to each item's
/// cost (<c>landed_cost_item_effects</c>: capitalized into the stock on hand, expensed for what was already sold, cost before and after).
/// Both are history: never updated.</item>
/// <item>Charges owed to a supplier become that supplier's <b>service bill</b> (<c>purchase_invoices.kind = 'SERVICE'</c>: lines carry
/// a charge type instead of an item, no warehouse, no stock), so what we owe them is paid and followed like any bill.</item>
/// </list>
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000028_AddLandedCost")]
public sealed class AddLandedCost : Migration
{
    public static readonly (string Table, string NumberColumn, string Series)[] NumberedTables =
    [
        ("landed_cost_vouchers", "voucher_number", "'LANDED_COST'"),
    ];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            -- ------------------------------------------------------------------ service bills
            ALTER TABLE purchase_invoices
                ADD COLUMN IF NOT EXISTS kind text NOT NULL DEFAULT 'GOODS' CHECK (kind IN ('GOODS', 'SERVICE')),
                ALTER COLUMN warehouse_id DROP NOT NULL,
                ADD CONSTRAINT ck_purchase_invoices_goods_warehouse CHECK (kind = 'SERVICE' OR warehouse_id IS NOT NULL),
                ADD CONSTRAINT ck_purchase_invoices_service_no_return CHECK (kind = 'GOODS' OR NOT is_return);

            ALTER TABLE purchase_invoice_lines
                ALTER COLUMN item_id DROP NOT NULL,
                ADD COLUMN IF NOT EXISTS charge_type text NULL CHECK (charge_type IN ('FREIGHT', 'CUSTOMS', 'SHIPPING', 'INSURANCE', 'OTHER')),
                ADD COLUMN IF NOT EXISTS description text NULL,
                ADD CONSTRAINT ck_purchase_invoice_lines_item_or_charge CHECK ((item_id IS NULL) = (charge_type IS NOT NULL));

            -- An item line belongs on a goods bill, a charge line on a service bill.
            CREATE OR REPLACE FUNCTION fn_purchase_line_kind() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                -- (the CASE is in brackets: PL/pgSQL would otherwise end the IF condition at the CASE's own THEN)
                IF (SELECT kind FROM purchase_invoices WHERE id = NEW.purchase_invoice_id) <> (CASE WHEN NEW.item_id IS NULL THEN 'SERVICE' ELSE 'GOODS' END) THEN
                    RAISE EXCEPTION 'An item line belongs on a goods bill and a charge line on a service bill';
                END IF;
                RETURN NEW;
            END;
            $$;
            DROP TRIGGER IF EXISTS trg_purchase_line_kind ON purchase_invoice_lines;
            CREATE TRIGGER trg_purchase_line_kind BEFORE INSERT OR UPDATE ON purchase_invoice_lines
                FOR EACH ROW EXECUTE FUNCTION fn_purchase_line_kind();

            -- ------------------------------------------------------------------ vouchers
            CREATE TABLE landed_cost_vouchers (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                voucher_number text NOT NULL UNIQUE,
                voucher_date date NOT NULL,
                status text NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT', 'POSTED', 'VOID')),
                fx_rate_id uuid NULL REFERENCES fx_rates(id),
                total_usd numeric(18,4) NOT NULL DEFAULT 0 CHECK (total_usd >= 0),
                notes text NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL,
                updated_at timestamptz NULL,
                posted_at timestamptz NULL,
                posted_by uuid NULL,
                voided_at timestamptz NULL,
                voided_by uuid NULL,
                void_reason text NULL
            );

            CREATE TABLE landed_cost_voucher_bills (
                voucher_id uuid NOT NULL REFERENCES landed_cost_vouchers(id),
                purchase_invoice_id uuid NOT NULL REFERENCES purchase_invoices(id),
                PRIMARY KEY (voucher_id, purchase_invoice_id)
            );
            CREATE INDEX ix_landed_cost_voucher_bills_bill ON landed_cost_voucher_bills (purchase_invoice_id);

            -- Only goods bills carry landed costs.
            CREATE OR REPLACE FUNCTION fn_landed_cost_bill_is_goods() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM purchase_invoices WHERE id = NEW.purchase_invoice_id AND kind = 'GOODS' AND NOT is_return) THEN
                    RAISE EXCEPTION 'Landed costs go on a goods bill (not a service bill or a return)';
                END IF;
                RETURN NEW;
            END;
            $$;
            CREATE TRIGGER trg_landed_cost_bill_is_goods BEFORE INSERT OR UPDATE ON landed_cost_voucher_bills
                FOR EACH ROW EXECUTE FUNCTION fn_landed_cost_bill_is_goods();

            CREATE TABLE landed_cost_charges (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                voucher_id uuid NOT NULL REFERENCES landed_cost_vouchers(id),
                line_number int NOT NULL,
                charge_type text NOT NULL CHECK (charge_type IN ('FREIGHT', 'CUSTOMS', 'SHIPPING', 'INSURANCE', 'OTHER')),
                description text NULL,
                amount_usd numeric(18,4) NOT NULL CHECK (amount_usd > 0),
                split_method text NOT NULL CHECK (split_method IN ('VALUE', 'QTY', 'EQUAL')),
                supplier_party_id uuid NULL REFERENCES parties(id),
                paid_from_account text NULL,
                CONSTRAINT uq_landed_cost_charges_line UNIQUE (voucher_id, line_number),
                -- owed to a supplier (a service bill) or paid at once from an account, never both
                CONSTRAINT ck_landed_cost_charges_settlement CHECK ((supplier_party_id IS NULL) <> (paid_from_account IS NULL))
            );

            CREATE TABLE landed_cost_allocations (
                voucher_id uuid NOT NULL REFERENCES landed_cost_vouchers(id),
                charge_id uuid NOT NULL REFERENCES landed_cost_charges(id),
                purchase_invoice_line_id uuid NOT NULL REFERENCES purchase_invoice_lines(id),
                amount_usd numeric(18,4) NOT NULL,
                PRIMARY KEY (charge_id, purchase_invoice_line_id)
            );
            CREATE INDEX ix_landed_cost_allocations_voucher ON landed_cost_allocations (voucher_id);

            CREATE TABLE landed_cost_item_effects (
                voucher_id uuid NOT NULL REFERENCES landed_cost_vouchers(id),
                sku_id uuid NOT NULL REFERENCES skus(id),
                quantity numeric(18,4) NOT NULL,
                on_hand numeric(18,4) NOT NULL,
                allocated_usd numeric(18,4) NOT NULL,
                capitalized_usd numeric(18,4) NOT NULL,
                expensed_usd numeric(18,4) NOT NULL,
                cost_before_usd numeric(18,4) NOT NULL,
                cost_after_usd numeric(18,4) NOT NULL,
                PRIMARY KEY (voucher_id, sku_id),
                CONSTRAINT ck_landed_cost_item_effects_split CHECK (capitalized_usd + expensed_usd = allocated_usd)
            );

            -- How a voucher was posted is history: it is never rewritten (a void reverses it, a deletion removes it whole).
            CREATE OR REPLACE FUNCTION fn_no_update() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION '% rows are never changed', TG_TABLE_NAME;
            END;
            $$;
            CREATE TRIGGER trg_landed_cost_allocations_no_update BEFORE UPDATE ON landed_cost_allocations FOR EACH ROW EXECUTE FUNCTION fn_no_update();
            CREATE TRIGGER trg_landed_cost_item_effects_no_update BEFORE UPDATE ON landed_cost_item_effects FOR EACH ROW EXECUTE FUNCTION fn_no_update();

            -- The service bills a voucher raised for its suppliers.
            ALTER TABLE purchase_invoices ADD COLUMN IF NOT EXISTS landed_cost_voucher_id uuid NULL REFERENCES landed_cost_vouchers(id) ON DELETE SET NULL;
            CREATE INDEX IF NOT EXISTS ix_purchase_invoices_landed_cost_voucher ON purchase_invoices (landed_cost_voucher_id) WHERE landed_cost_voucher_id IS NOT NULL;

            -- ------------------------------------------------------------------ numbering (series LCV)
            CREATE OR REPLACE FUNCTION seed_document_series() RETURNS void LANGUAGE sql AS $$
                INSERT INTO document_series (code, prefix, name_ar) VALUES
                    ('SALES_INVOICE',    'INV',  'فاتورة مبيعات'),
                    ('SALES_RETURN',     'SRN',  'مرتجع مبيعات'),
                    ('CREDIT_NOTE',      'CRN',  'إشعار دائن لإلغاء فاتورة'),
                    ('CUSTOMER_PAYMENT', 'PAY',  'سند قبض من عميل'),
                    ('PURCHASE_INVOICE', 'PUR',  'فاتورة مشتريات'),
                    ('PURCHASE_RETURN',  'PRN',  'مرتجع مشتريات'),
                    ('LANDED_COST',      'LCV',  'قيد رسملة مصاريف الشراء'),
                    ('SUPPLIER_PAYMENT', 'SPAY', 'دفعة لمورد'),
                    ('STOCK_ADJUSTMENT', 'ADJ',  'تسوية مخزون'),
                    ('TRANSFER_ORDER',   'TRF',  'أمر تحويل'),
                    ('RECEIVING',        'RCV',  'سند استلام'),
                    ('ISSUE_ORDER',      'ISO',  'أمر صرف')
                ON CONFLICT (code) DO NOTHING;
                INSERT INTO document_series (code, prefix, name_ar)
                SELECT 'JE.' || code, prefix, name_ar FROM entry_types
                ON CONFLICT (code) DO NOTHING;
            $$;
            SELECT seed_document_series();
            """);

        foreach (var (table, number, series) in NumberedTables)
        {
            migrationBuilder.Sql(AddDocumentNumbering.NumberingSql(table, number, series));
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Landed costs are not reversible: restore a backup taken before this migration.");
    }
}
