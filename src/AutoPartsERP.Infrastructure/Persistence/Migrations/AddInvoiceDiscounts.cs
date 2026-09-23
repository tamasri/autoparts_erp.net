using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// A discount on the whole invoice, on top of each line's own discount, for sales and purchase invoices. It is a percentage of the lines
/// (<c>discount_pct</c>) or a fixed amount (<c>discount_pct</c> null, <c>discount_amount_*</c> set). Sales invoices already had the amount
/// columns, unused. <c>recalc_invoice_totals</c> is now the only place a sales invoice's totals are worked out (it replaces three copies of the
/// same SQL, one of which ignored the sign of returns).
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000021_AddInvoiceDiscounts")]
public sealed class AddInvoiceDiscounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE invoices ADD COLUMN discount_pct numeric(5,2) CHECK (discount_pct IS NULL OR (discount_pct >= 0 AND discount_pct <= 100));

            ALTER TABLE purchase_invoices
                ADD COLUMN subtotal_usd numeric(18,4) NOT NULL DEFAULT 0,
                ADD COLUMN discount_pct numeric(5,2) CHECK (discount_pct IS NULL OR (discount_pct >= 0 AND discount_pct <= 100)),
                ADD COLUMN discount_amount_usd numeric(18,4) NOT NULL DEFAULT 0 CHECK (discount_amount_usd >= 0);
            UPDATE purchase_invoices SET subtotal_usd = total_usd;

            -- Totals of a sales invoice from its lines. The discount is a positive amount; a RETURN's subtotal and total are negative.
            CREATE OR REPLACE FUNCTION recalc_invoice_totals(p_invoice_id uuid) RETURNS void LANGUAGE sql AS $$
                WITH sums AS (
                    SELECT COALESCE(SUM(line_total_syp), 0) AS syp, COALESCE(SUM(line_total_usd), 0) AS usd
                    FROM invoice_lines WHERE invoice_id = p_invoice_id
                ), d AS (
                    SELECT i.id, s.syp, s.usd,
                           CASE WHEN i.invoice_type = 'RETURN' THEN -1 ELSE 1 END AS sign,
                           CASE WHEN i.discount_pct IS NOT NULL THEN round(s.syp * i.discount_pct / 100, 4) ELSE LEAST(i.discount_amount_syp, s.syp) END AS dsyp,
                           CASE WHEN i.discount_pct IS NOT NULL THEN round(s.usd * i.discount_pct / 100, 4) ELSE LEAST(i.discount_amount_usd, s.usd) END AS dusd
                    FROM invoices i CROSS JOIN sums s
                    WHERE i.id = p_invoice_id
                )
                UPDATE invoices i
                SET subtotal_syp = d.sign * d.syp,
                    subtotal_usd = d.sign * d.usd,
                    discount_amount_syp = d.dsyp,
                    discount_amount_usd = d.dusd,
                    total_syp = d.sign * (d.syp - d.dsyp + i.delivery_fee_syp + i.tax_amount_syp),
                    total_usd = d.sign * (d.usd - d.dusd + i.delivery_fee_usd + i.tax_amount_usd)
                FROM d
                WHERE i.id = d.id;
            $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP FUNCTION IF EXISTS recalc_invoice_totals(uuid);
            ALTER TABLE purchase_invoices DROP COLUMN IF EXISTS discount_amount_usd, DROP COLUMN IF EXISTS discount_pct, DROP COLUMN IF EXISTS subtotal_usd;
            ALTER TABLE invoices DROP COLUMN IF EXISTS discount_pct;
            """);
    }
}
