using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// Purchasing: supplier bills (purchase invoices) that receive goods into a warehouse and update the item cost, and supplier
/// payments allocated to one or more bills. Amounts are in USD, the ledger currency; the FX rate used is kept for reference.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000017_AddPurchasing")]
public sealed class AddPurchasing : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE SEQUENCE IF NOT EXISTS purchase_invoice_seq START 1;
            CREATE SEQUENCE IF NOT EXISTS supplier_payment_seq START 1;

            CREATE TABLE purchase_invoices (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                bill_number text NOT NULL UNIQUE,
                supplier_party_id uuid NOT NULL REFERENCES parties(id),
                supplier_ref text,
                bill_date date NOT NULL,
                due_date date NOT NULL,
                warehouse_id uuid NOT NULL REFERENCES locations(id),
                status text NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT', 'POSTED', 'VOID')),
                fx_rate_id uuid REFERENCES fx_rates(id),
                total_usd numeric(18,4) NOT NULL DEFAULT 0,
                paid_usd numeric(18,4) NOT NULL DEFAULT 0,
                balance_usd numeric(18,4) NOT NULL DEFAULT 0,
                notes text,
                void_reason text,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL REFERENCES asp_net_users(id),
                posted_at timestamptz,
                posted_by uuid REFERENCES asp_net_users(id),
                voided_at timestamptz,
                voided_by uuid REFERENCES asp_net_users(id),
                updated_at timestamptz,
                CONSTRAINT ck_purchase_invoices_amounts CHECK (total_usd >= 0 AND paid_usd >= 0 AND balance_usd >= 0)
            );
            CREATE INDEX idx_purchase_invoices_supplier ON purchase_invoices (supplier_party_id, bill_date DESC);
            CREATE INDEX idx_purchase_invoices_status ON purchase_invoices (status, bill_date DESC);

            CREATE TABLE purchase_invoice_lines (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                purchase_invoice_id uuid NOT NULL REFERENCES purchase_invoices(id) ON DELETE CASCADE,
                line_number int NOT NULL,
                item_id uuid NOT NULL REFERENCES items(id),
                quantity numeric(18,4) NOT NULL CHECK (quantity > 0),
                unit_cost_usd numeric(18,4) NOT NULL CHECK (unit_cost_usd >= 0),
                discount_pct numeric(5,2) NOT NULL DEFAULT 0 CHECK (discount_pct >= 0 AND discount_pct <= 100),
                line_total_usd numeric(18,4) NOT NULL,
                UNIQUE (purchase_invoice_id, line_number)
            );

            CREATE TABLE supplier_payments (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                payment_number text NOT NULL UNIQUE,
                supplier_party_id uuid NOT NULL REFERENCES parties(id),
                payment_date date NOT NULL,
                payment_method text NOT NULL CHECK (payment_method IN ('CASH', 'BANK_TRANSFER', 'CHEQUE', 'USD_CASH')),
                amount_usd numeric(18,4) NOT NULL CHECK (amount_usd > 0),
                reference_number text,
                bank_name text,
                cheque_number text,
                notes text,
                is_reversed boolean NOT NULL DEFAULT FALSE,
                reverse_reason text,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL REFERENCES asp_net_users(id),
                reversed_at timestamptz,
                reversed_by uuid REFERENCES asp_net_users(id)
            );
            CREATE INDEX idx_supplier_payments_supplier ON supplier_payments (supplier_party_id, payment_date DESC);

            CREATE TABLE supplier_payment_allocations (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                supplier_payment_id uuid NOT NULL REFERENCES supplier_payments(id),
                purchase_invoice_id uuid NOT NULL REFERENCES purchase_invoices(id),
                allocated_usd numeric(18,4) NOT NULL CHECK (allocated_usd > 0)
            );
            CREATE INDEX idx_supplier_alloc_invoice ON supplier_payment_allocations (purchase_invoice_id);

            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
