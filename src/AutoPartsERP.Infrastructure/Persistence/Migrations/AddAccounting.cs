using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// Accounting on top of the ERPNext ledger: user-defined entry types (receipt, payment, contra, journal, ...), manual entries with
/// their lines, tags that can be put on any entry or ledger voucher, and ledger-account reconciliations. The ledger itself stays in
/// ERPNext; these tables hold only what ERPNext has no place for. Also renames the customer role label to "زبون".
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000019_AddAccounting")]
public sealed class AddAccounting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE entry_types (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                code text NOT NULL UNIQUE,
                name_ar text NOT NULL,
                kind text NOT NULL CHECK (kind IN ('RECEIPT', 'PAYMENT', 'CONTRA', 'JOURNAL', 'OPENING', 'DEBIT_NOTE', 'CREDIT_NOTE')),
                prefix text NOT NULL UNIQUE CHECK (prefix ~ '^[A-Z]{1,6}$'),
                description text,
                is_system boolean NOT NULL DEFAULT FALSE,
                is_active boolean NOT NULL DEFAULT TRUE,
                last_number integer NOT NULL DEFAULT 0,
                created_at timestamptz NOT NULL DEFAULT now()
            );

            INSERT INTO entry_types (code, name_ar, kind, prefix, description, is_system) VALUES
                ('RECEIPT',     'سند قبض',      'RECEIPT',     'RV', 'استلام مبلغ في الصندوق أو المصرف مقابل حساب آخر', TRUE),
                ('PAYMENT',     'سند دفع',      'PAYMENT',     'PV', 'دفع مبلغ من الصندوق أو المصرف لحساب آخر', TRUE),
                ('CONTRA',      'قيد مناقلة',    'CONTRA',      'CV', 'نقل مبلغ بين الصندوق والمصارف', TRUE),
                ('JOURNAL',     'قيد يومية',     'JOURNAL',     'JV', 'قيد عام بأي عدد من الأسطر', TRUE),
                ('OPENING',     'قيد افتتاحي',   'OPENING',     'OV', 'أرصدة أول المدة', TRUE),
                ('DEBIT_NOTE',  'إشعار مدين',    'DEBIT_NOTE',  'DN', 'إشعار مدين لحساب', TRUE),
                ('CREDIT_NOTE', 'إشعار دائن',    'CREDIT_NOTE', 'CN', 'إشعار دائن لحساب', TRUE);

            CREATE TABLE journal_entries (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                entry_number text NOT NULL UNIQUE,
                entry_type_id uuid NOT NULL REFERENCES entry_types(id),
                entry_date date NOT NULL,
                status text NOT NULL DEFAULT 'DRAFT' CHECK (status IN ('DRAFT', 'POSTED', 'VOID')),
                narration text,
                reference_number text,
                total_usd numeric(18,4) NOT NULL DEFAULT 0,
                created_by uuid,
                posted_by uuid,
                posted_at timestamptz,
                voided_by uuid,
                voided_at timestamptz,
                void_reason text,
                created_at timestamptz NOT NULL DEFAULT now(),
                updated_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX ix_journal_entries_date ON journal_entries (entry_date DESC, created_at DESC);

            CREATE TABLE journal_entry_lines (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                journal_entry_id uuid NOT NULL REFERENCES journal_entries(id) ON DELETE CASCADE,
                line_number integer NOT NULL,
                account_name text NOT NULL,
                party_id uuid REFERENCES parties(id),
                debit_usd numeric(18,4) NOT NULL DEFAULT 0 CHECK (debit_usd >= 0),
                credit_usd numeric(18,4) NOT NULL DEFAULT 0 CHECK (credit_usd >= 0),
                narration text,
                CHECK (NOT (debit_usd > 0 AND credit_usd > 0)),
                UNIQUE (journal_entry_id, line_number)
            );

            CREATE TABLE accounting_tags (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                name text NOT NULL,
                color text NOT NULL DEFAULT '#5c54ff',
                created_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE UNIQUE INDEX ux_accounting_tags_name ON accounting_tags (lower(name));

            -- A tag sits on a manual entry (target_type JOURNAL_ENTRY, key = its id) or on any ERPNext voucher
            -- (target_type ERPNEXT, key = "<voucher type>|<voucher number>"), so ledger rows can be tagged too.
            CREATE TABLE accounting_tag_links (
                tag_id uuid NOT NULL REFERENCES accounting_tags(id) ON DELETE CASCADE,
                target_type text NOT NULL CHECK (target_type IN ('JOURNAL_ENTRY', 'ERPNEXT')),
                target_key text NOT NULL,
                PRIMARY KEY (tag_id, target_type, target_key)
            );
            CREATE INDEX ix_accounting_tag_links_target ON accounting_tag_links (target_type, target_key);

            CREATE TABLE ledger_reconciliations (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                account_name text NOT NULL,
                statement_date date NOT NULL,
                statement_balance numeric(18,4) NOT NULL,
                cleared_total numeric(18,4) NOT NULL,
                notes text,
                completed_by uuid,
                completed_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX ix_ledger_reconciliations_account ON ledger_reconciliations (account_name, statement_date DESC);

            -- signed_amount is debit - credit; a ledger line can be reconciled only once.
            CREATE TABLE ledger_reconciliation_items (
                reconciliation_id uuid NOT NULL REFERENCES ledger_reconciliations(id) ON DELETE CASCADE,
                account_name text NOT NULL,
                gl_entry_name text NOT NULL,
                posting_date date NOT NULL,
                voucher_type text,
                voucher_no text,
                signed_amount numeric(18,4) NOT NULL,
                PRIMARY KEY (reconciliation_id, gl_entry_name),
                UNIQUE (account_name, gl_entry_name)
            );

            UPDATE party_type_catalog SET label_ar = 'زبون' WHERE code = 'CUSTOMER' AND label_ar <> 'زبون';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS ledger_reconciliation_items;
            DROP TABLE IF EXISTS ledger_reconciliations;
            DROP TABLE IF EXISTS accounting_tag_links;
            DROP TABLE IF EXISTS accounting_tags;
            DROP TABLE IF EXISTS journal_entry_lines;
            DROP TABLE IF EXISTS journal_entries;
            DROP TABLE IF EXISTS entry_types;
            """);
    }
}
