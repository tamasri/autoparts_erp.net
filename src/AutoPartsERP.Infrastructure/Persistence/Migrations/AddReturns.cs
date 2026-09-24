using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// Sales and purchase returns the way ERPNext has them (<c>is_return</c> + <c>return_against</c>): a return is its own numbered
/// document tied to the invoice it returns, line by line, and can never give back more than that invoice held.
/// <list type="bullet">
/// <item>Sales: a RETURN points at its SALE (<c>original_invoice_id</c>) and each of its lines at the sold line
/// (<c>return_of_line_id</c>, same SKU). Purchases: <c>is_return</c>, <c>return_against_id</c>, lines likewise; purchase returns get
/// their own series (PRN) and, like sales returns, negative totals.</item>
/// <item>A posted return's credit is applied to the invoice it returns (<c>credit_applied_*</c>: + on that invoice, − on the return),
/// so the invoice's outstanding falls as ERPNext's does; what exceeds the outstanding stays on the return as the party's credit.
/// Balances are computed by the database: total − paid − credit applied (a purchase bill counts only while POSTED).</item>
/// <item>Triggers refuse a return line that is not a line of the returned invoice, and a return whose posting would take more back
/// than was sold or bought (all posted returns together).</item>
/// <item><c>monthly_pl_summary</c> counted returned lines as sales; returns now reduce revenue and cost.</item>
/// </list>
/// Rules that trial data from before this migration cannot meet (free-standing returns) are added NOT VALID; the business-data
/// reset validates every such constraint once the old rows are gone.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000027_AddReturns")]
public sealed class AddReturns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ------------------------------------------------------------------ sales returns
        migrationBuilder.Sql("""
            ALTER TABLE invoice_lines ADD COLUMN IF NOT EXISTS return_of_line_id uuid NULL REFERENCES invoice_lines(id);
            CREATE INDEX IF NOT EXISTS ix_invoice_lines_return_of ON invoice_lines (return_of_line_id) WHERE return_of_line_id IS NOT NULL;
            CREATE INDEX IF NOT EXISTS ix_invoices_original ON invoices (original_invoice_id) WHERE original_invoice_id IS NOT NULL;

            ALTER TABLE invoices
                ADD COLUMN IF NOT EXISTS credit_applied_syp numeric(18,4) NOT NULL DEFAULT 0,
                ADD COLUMN IF NOT EXISTS credit_applied_usd numeric(18,4) NOT NULL DEFAULT 0;

            DROP MATERIALIZED VIEW IF EXISTS customer_account_summary;
            ALTER TABLE invoices DROP COLUMN balance_syp, DROP COLUMN balance_usd;
            ALTER TABLE invoices
                ADD COLUMN balance_syp numeric(18,4) GENERATED ALWAYS AS (total_syp - paid_syp - credit_applied_syp) STORED,
                ADD COLUMN balance_usd numeric(18,4) GENERATED ALWAYS AS (total_usd - paid_usd - credit_applied_usd) STORED;
            CREATE MATERIALIZED VIEW customer_account_summary AS
            SELECT c.id AS customer_id,
                   COALESCE(SUM(i.total_syp), 0) AS total_invoiced_syp, COALESCE(SUM(i.total_usd), 0) AS total_invoiced_usd,
                   COALESCE(SUM(i.paid_syp), 0) AS total_paid_syp, COALESCE(SUM(i.paid_usd), 0) AS total_paid_usd,
                   COALESCE(SUM(i.balance_syp), 0) AS outstanding_syp, COALESCE(SUM(i.balance_usd), 0) AS outstanding_usd
            FROM customers c LEFT JOIN invoices i ON i.customer_id = c.id
            GROUP BY c.id;
            CREATE UNIQUE INDEX ux_customer_account_summary_customer_id ON customer_account_summary (customer_id);

            -- A return answers one sale.
            ALTER TABLE invoices ADD CONSTRAINT ck_invoices_return_has_original
                CHECK (invoice_type <> 'RETURN' OR original_invoice_id IS NOT NULL) NOT VALID;

            -- A return line is a line of the sale it returns, for the same SKU; other lines never point anywhere.
            CREATE OR REPLACE FUNCTION fn_invoice_line_return_link() RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE v_type text; v_original uuid;
            BEGIN
                SELECT invoice_type, original_invoice_id INTO v_type, v_original FROM invoices WHERE id = NEW.invoice_id;
                IF v_type = 'RETURN' THEN
                    IF NEW.return_of_line_id IS NULL OR NOT EXISTS (
                        SELECT 1 FROM invoice_lines o WHERE o.id = NEW.return_of_line_id AND o.invoice_id = v_original AND o.sku_id = NEW.sku_id) THEN
                        RAISE EXCEPTION 'A return line must be a line of the returned invoice, for the same item';
                    END IF;
                ELSIF NEW.return_of_line_id IS NOT NULL THEN
                    RAISE EXCEPTION 'Only a return line refers to another invoice line';
                END IF;
                RETURN NEW;
            END;
            $$;
            DROP TRIGGER IF EXISTS trg_invoice_line_return_link ON invoice_lines;
            CREATE TRIGGER trg_invoice_line_return_link BEFORE INSERT OR UPDATE ON invoice_lines
                FOR EACH ROW EXECUTE FUNCTION fn_invoice_line_return_link();

            -- Posting a return never takes back more than was sold, counting every posted return of each line.
            CREATE OR REPLACE FUNCTION fn_invoice_return_within_sold() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM invoice_lines r
                    INNER JOIN invoice_lines o ON o.id = r.return_of_line_id
                    WHERE r.invoice_id = NEW.id
                      AND (SELECT COALESCE(SUM(x.quantity), 0) FROM invoice_lines x INNER JOIN invoices xi ON xi.id = x.invoice_id
                           WHERE x.return_of_line_id = o.id AND xi.status = 'POSTED') > o.quantity) THEN
                    RAISE EXCEPTION 'Return % would take back more than was sold', NEW.invoice_number;
                END IF;
                RETURN NEW;
            END;
            $$;
            DROP TRIGGER IF EXISTS trg_invoice_return_within_sold ON invoices;
            CREATE TRIGGER trg_invoice_return_within_sold AFTER UPDATE OF status ON invoices
                FOR EACH ROW WHEN (NEW.invoice_type = 'RETURN' AND NEW.status = 'POSTED' AND OLD.status IS DISTINCT FROM 'POSTED')
                EXECUTE FUNCTION fn_invoice_return_within_sold();
            """);

        // ------------------------------------------------------------------ purchase returns
        migrationBuilder.Sql("""
            ALTER TABLE purchase_invoices
                ADD COLUMN IF NOT EXISTS is_return boolean NOT NULL DEFAULT FALSE,
                ADD COLUMN IF NOT EXISTS return_against_id uuid NULL REFERENCES purchase_invoices(id),
                ADD COLUMN IF NOT EXISTS credit_applied_usd numeric(18,4) NOT NULL DEFAULT 0;
            CREATE INDEX IF NOT EXISTS ix_purchase_invoices_return_against ON purchase_invoices (return_against_id) WHERE return_against_id IS NOT NULL;
            ALTER TABLE purchase_invoice_lines ADD COLUMN IF NOT EXISTS return_of_line_id uuid NULL REFERENCES purchase_invoice_lines(id);
            CREATE INDEX IF NOT EXISTS ix_purchase_invoice_lines_return_of ON purchase_invoice_lines (return_of_line_id) WHERE return_of_line_id IS NOT NULL;

            -- A return is a negative document (like a sales return); the balance is the database's, not maintained by hand.
            ALTER TABLE purchase_invoices DROP CONSTRAINT IF EXISTS ck_purchase_invoices_amounts;
            ALTER TABLE purchase_invoices DROP COLUMN balance_usd;
            ALTER TABLE purchase_invoices ADD COLUMN balance_usd numeric(18,4)
                GENERATED ALWAYS AS (CASE WHEN status = 'POSTED' THEN total_usd - paid_usd - credit_applied_usd ELSE 0 END) STORED;
            ALTER TABLE purchase_invoices
                ADD CONSTRAINT ck_purchase_invoices_amounts CHECK (paid_usd >= 0 AND CASE WHEN is_return THEN total_usd <= 0 ELSE total_usd >= 0 END),
                ADD CONSTRAINT ck_purchase_invoices_return_link CHECK (is_return = (return_against_id IS NOT NULL));

            CREATE OR REPLACE FUNCTION fn_purchase_line_return_link() RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE v_against uuid;
            BEGIN
                SELECT return_against_id INTO v_against FROM purchase_invoices WHERE id = NEW.purchase_invoice_id;
                IF v_against IS NOT NULL THEN
                    IF NEW.return_of_line_id IS NULL OR NOT EXISTS (
                        SELECT 1 FROM purchase_invoice_lines o WHERE o.id = NEW.return_of_line_id AND o.purchase_invoice_id = v_against AND o.item_id = NEW.item_id) THEN
                        RAISE EXCEPTION 'A return line must be a line of the returned bill, for the same item';
                    END IF;
                ELSIF NEW.return_of_line_id IS NOT NULL THEN
                    RAISE EXCEPTION 'Only a return line refers to another bill line';
                END IF;
                RETURN NEW;
            END;
            $$;
            DROP TRIGGER IF EXISTS trg_purchase_line_return_link ON purchase_invoice_lines;
            CREATE TRIGGER trg_purchase_line_return_link BEFORE INSERT OR UPDATE ON purchase_invoice_lines
                FOR EACH ROW EXECUTE FUNCTION fn_purchase_line_return_link();

            CREATE OR REPLACE FUNCTION fn_purchase_return_within_bought() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM purchase_invoice_lines r
                    INNER JOIN purchase_invoice_lines o ON o.id = r.return_of_line_id
                    WHERE r.purchase_invoice_id = NEW.id
                      AND (SELECT COALESCE(SUM(x.quantity), 0) FROM purchase_invoice_lines x INNER JOIN purchase_invoices xp ON xp.id = x.purchase_invoice_id
                           WHERE x.return_of_line_id = o.id AND xp.status = 'POSTED') > o.quantity) THEN
                    RAISE EXCEPTION 'Return % would give back more than was bought', NEW.bill_number;
                END IF;
                RETURN NEW;
            END;
            $$;
            DROP TRIGGER IF EXISTS trg_purchase_return_within_bought ON purchase_invoices;
            CREATE TRIGGER trg_purchase_return_within_bought AFTER UPDATE OF status ON purchase_invoices
                FOR EACH ROW WHEN (NEW.is_return AND NEW.status = 'POSTED' AND OLD.status IS DISTINCT FROM 'POSTED')
                EXECUTE FUNCTION fn_purchase_return_within_bought();

            -- Purchase returns are numbered in their own series.
            CREATE OR REPLACE FUNCTION seed_document_series() RETURNS void LANGUAGE sql AS $$
                INSERT INTO document_series (code, prefix, name_ar) VALUES
                    ('SALES_INVOICE',    'INV',  'فاتورة مبيعات'),
                    ('SALES_RETURN',     'SRN',  'مرتجع مبيعات'),
                    ('CREDIT_NOTE',      'CRN',  'إشعار دائن لإلغاء فاتورة'),
                    ('CUSTOMER_PAYMENT', 'PAY',  'سند قبض من عميل'),
                    ('PURCHASE_INVOICE', 'PUR',  'فاتورة مشتريات'),
                    ('PURCHASE_RETURN',  'PRN',  'مرتجع مشتريات'),
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

            CREATE OR REPLACE FUNCTION fn_purchase_invoices_number() RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE c record;
            BEGIN
                NEW.series_code := CASE WHEN NEW.is_return THEN 'PURCHASE_RETURN' ELSE 'PURCHASE_INVOICE' END;
                SELECT * INTO c FROM claim_document_number(NEW.series_code);
                NEW.serial_no := c.serial;
                NEW.bill_number := c.number;
                RETURN NEW;
            END;
            $$;

            CREATE OR REPLACE FUNCTION fn_purchase_invoices_number_fixed() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.series_code IS DISTINCT FROM OLD.series_code OR NEW.serial_no IS DISTINCT FROM OLD.serial_no
                   OR NEW.bill_number IS DISTINCT FROM OLD.bill_number
                   OR (CASE WHEN NEW.is_return THEN 'PURCHASE_RETURN' ELSE 'PURCHASE_INVOICE' END) IS DISTINCT FROM OLD.series_code
                   OR NEW.return_against_id IS DISTINCT FROM OLD.return_against_id THEN
                    RAISE EXCEPTION 'The number and kind of document purchase_invoices % never change', OLD.bill_number;
                END IF;
                RETURN NEW;
            END;
            $$;
            """);

        // ------------------------------------------------------------------ profit: returns reduce sales and cost
        migrationBuilder.Sql("""
            DROP MATERIALIZED VIEW IF EXISTS monthly_pl_summary;
            CREATE MATERIALIZED VIEW monthly_pl_summary AS
            WITH l AS (
                SELECT i.invoice_date, CASE WHEN i.invoice_type = 'RETURN' THEN -1 ELSE 1 END AS sign,
                       il.line_total_syp, il.line_total_usd, il.quantity * il.cost_price_syp AS cost_syp, il.quantity * il.cost_price_usd AS cost_usd
                FROM invoice_lines il INNER JOIN invoices i ON i.id = il.invoice_id
                WHERE i.status = 'POSTED' AND i.invoice_type IN ('SALE', 'RETURN')
            )
            SELECT EXTRACT(year FROM invoice_date)::int AS year, EXTRACT(month FROM invoice_date)::int AS month,
                   COALESCE(SUM(sign * line_total_syp), 0) AS total_revenue_syp, COALESCE(SUM(sign * line_total_usd), 0) AS total_revenue_usd,
                   COALESCE(SUM(sign * cost_syp), 0) AS total_cogs_syp, COALESCE(SUM(sign * cost_usd), 0) AS total_cogs_usd,
                   COALESCE(SUM(sign * (line_total_syp - cost_syp)), 0) AS gross_profit_syp,
                   COALESCE(SUM(sign * (line_total_usd - cost_usd)), 0) AS gross_profit_usd,
                   CASE WHEN COALESCE(SUM(sign * line_total_syp), 0) = 0 THEN 0
                        ELSE SUM(sign * (line_total_syp - cost_syp)) / SUM(sign * line_total_syp) * 100 END AS gross_margin_pct
            FROM l
            GROUP BY EXTRACT(year FROM invoice_date), EXTRACT(month FROM invoice_date);
            CREATE UNIQUE INDEX ux_monthly_pl_summary_year_month ON monthly_pl_summary (year, month);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Returns are not reversible: restore a backup taken before this migration.");
    }
}
