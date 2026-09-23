using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// Sales representatives. <c>invoices.sales_rep_id</c> and <c>customers.assigned_sales_rep</c> already held user ids with nothing behind
/// them; a rep is now a user with a row here (commission, monthly target, active). Existing references to users are kept as reps,
/// references to anything else are cleared, and both columns get a foreign key so a rep cannot be deleted from under an invoice.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000022_AddSalesReps")]
public sealed class AddSalesReps : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE sales_reps (
                user_id uuid PRIMARY KEY REFERENCES asp_net_users(id),
                commission_pct numeric(5,2) NOT NULL DEFAULT 0 CHECK (commission_pct >= 0 AND commission_pct <= 100),
                monthly_target_usd numeric(18,4) NOT NULL DEFAULT 0 CHECK (monthly_target_usd >= 0),
                is_active boolean NOT NULL DEFAULT TRUE,
                notes text,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid,
                updated_at timestamptz,
                updated_by uuid
            );

            UPDATE invoices SET sales_rep_id = NULL
            WHERE sales_rep_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM asp_net_users u WHERE u.id = invoices.sales_rep_id);
            UPDATE customers SET assigned_sales_rep = NULL
            WHERE assigned_sales_rep IS NOT NULL AND NOT EXISTS (SELECT 1 FROM asp_net_users u WHERE u.id = customers.assigned_sales_rep);

            INSERT INTO sales_reps (user_id)
            SELECT sales_rep_id FROM invoices WHERE sales_rep_id IS NOT NULL
            UNION
            SELECT assigned_sales_rep FROM customers WHERE assigned_sales_rep IS NOT NULL
            ON CONFLICT DO NOTHING;

            ALTER TABLE invoices ADD CONSTRAINT fk_invoices_sales_rep FOREIGN KEY (sales_rep_id) REFERENCES sales_reps(user_id);
            ALTER TABLE customers ADD CONSTRAINT fk_customers_sales_rep FOREIGN KEY (assigned_sales_rep) REFERENCES sales_reps(user_id);
            CREATE INDEX ix_invoices_sales_rep ON invoices (sales_rep_id, invoice_date) WHERE sales_rep_id IS NOT NULL;
            CREATE INDEX ix_customers_sales_rep ON customers (assigned_sales_rep) WHERE assigned_sales_rep IS NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS ix_customers_sales_rep;
            DROP INDEX IF EXISTS ix_invoices_sales_rep;
            ALTER TABLE customers DROP CONSTRAINT IF EXISTS fk_customers_sales_rep;
            ALTER TABLE invoices DROP CONSTRAINT IF EXISTS fk_invoices_sales_rep;
            DROP TABLE IF EXISTS sales_reps;
            """);
    }
}
