using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// Document numbers owned by the database (the model of ERPNext's naming series, without its gaps):
/// <list type="bullet">
/// <item><c>document_series</c> holds one counter per kind of document (and per accounting entry type). A number is claimed by a
/// row-locked <c>UPDATE … RETURNING</c> inside the inserting transaction, so a rolled-back insert gives its number back: no gaps,
/// no duplicates, strictly 1, 2, 3 … per series. A counter never goes down and a prefix never changes once it was used.</item>
/// <item>A <c>BEFORE INSERT</c> trigger on every numbered table sets <c>series_code</c>, <c>serial_no</c> and the printed number itself,
/// whatever the caller supplied — no code path can choose, reuse or skip a number. A <c>BEFORE UPDATE</c> trigger refuses any change
/// of the three, or of what decides the series (invoice type, entry type).</item>
/// <item>Deleting a numbered document requires its row in <c>deleted_documents</c> first (who, when, why, full snapshot); a
/// <c>BEFORE DELETE</c> trigger refuses any other delete. The number stays used, so every gap in a series is explained.</item>
/// </list>
/// Existing documents keep their printed numbers; they get serials in creation order and the counters continue from there.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000026_AddDocumentNumbering")]
public sealed class AddDocumentNumbering : Migration
{
    /// <summary>Table, printed-number column, and the SQL that gives a row's series (<c>{r}</c> = the row).</summary>
    public static readonly (string Table, string NumberColumn, string Series)[] NumberedTables =
    [
        ("invoices", "invoice_number",
            "CASE {r}.invoice_type WHEN 'SALE' THEN 'SALES_INVOICE' WHEN 'RETURN' THEN 'SALES_RETURN' WHEN 'CREDIT_NOTE' THEN 'CREDIT_NOTE' END"),
        ("payments", "payment_number", "'CUSTOMER_PAYMENT'"),
        ("purchase_invoices", "bill_number", "'PURCHASE_INVOICE'"),
        ("supplier_payments", "payment_number", "'SUPPLIER_PAYMENT'"),
        ("journal_entries", "entry_number", "(SELECT 'JE.' || t.code FROM entry_types t WHERE t.id = {r}.entry_type_id)"),
        ("stock_adjustments", "adjustment_no", "'STOCK_ADJUSTMENT'"),
        ("transfer_orders", "transfer_no", "'TRANSFER_ORDER'"),
        ("receiving_documents", "document_no", "'RECEIVING'"),
        ("issue_orders", "order_no", "'ISSUE_ORDER'"),
    ];

    private static readonly string[] OldSequences =
    [
        "invoice_number_seq", "payment_number_seq", "purchase_invoice_seq", "supplier_payment_seq", "stock_adjustment_seq", "transfer_order_seq",
        "receiving_document_seq", "issue_order_seq",
    ];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS document_series (
                code text PRIMARY KEY,
                prefix text NOT NULL UNIQUE CHECK (prefix ~ '^[A-Z][A-Z0-9]{0,9}$'),
                name_ar text NOT NULL,
                last_number bigint NOT NULL DEFAULT 0 CHECK (last_number >= 0),
                updated_at timestamptz NOT NULL DEFAULT now()
            );

            -- A counter only moves forward, and the prefix is frozen once a number was printed with it.
            CREATE OR REPLACE FUNCTION fn_document_series_guard() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF TG_OP = 'DELETE' THEN
                    RAISE EXCEPTION 'Document series "%" cannot be deleted', OLD.code;
                END IF;
                IF NEW.code IS DISTINCT FROM OLD.code THEN
                    RAISE EXCEPTION 'A document series code never changes ("%")', OLD.code;
                END IF;
                IF NEW.last_number < OLD.last_number THEN
                    RAISE EXCEPTION 'Document series "%" cannot go back from % to %', OLD.code, OLD.last_number, NEW.last_number;
                END IF;
                IF NEW.prefix IS DISTINCT FROM OLD.prefix AND OLD.last_number > 0 THEN
                    RAISE EXCEPTION 'The prefix of "%" is already printed on documents and cannot change', OLD.code;
                END IF;
                RETURN NEW;
            END;
            $$;
            DROP TRIGGER IF EXISTS trg_document_series_guard ON document_series;
            CREATE TRIGGER trg_document_series_guard BEFORE UPDATE OR DELETE ON document_series
                FOR EACH ROW EXECUTE FUNCTION fn_document_series_guard();

            CREATE OR REPLACE FUNCTION format_document_number(p_prefix text, p_serial bigint) RETURNS text LANGUAGE sql IMMUTABLE AS $$
                SELECT p_prefix || '-' || CASE WHEN p_serial < 1000000 THEN lpad(p_serial::text, 6, '0') ELSE p_serial::text END;
            $$;

            -- The only way a number is handed out. The row lock is held until the caller's transaction ends, so a rollback returns it.
            CREATE OR REPLACE FUNCTION claim_document_number(p_series text, OUT serial bigint, OUT number text) LANGUAGE plpgsql AS $$
            DECLARE v_prefix text;
            BEGIN
                UPDATE document_series SET last_number = last_number + 1, updated_at = now()
                WHERE code = p_series
                RETURNING last_number, prefix INTO serial, v_prefix;
                IF serial IS NULL THEN
                    RAISE EXCEPTION 'Unknown document series "%"', p_series;
                END IF;
                number := format_document_number(v_prefix, serial);
            END;
            $$;

            -- Fixed series, plus one per accounting entry type. Also run after a data reset (the reset empties document_series).
            CREATE OR REPLACE FUNCTION seed_document_series() RETURNS void LANGUAGE sql AS $$
                INSERT INTO document_series (code, prefix, name_ar) VALUES
                    ('SALES_INVOICE',    'INV',  'فاتورة مبيعات'),
                    ('SALES_RETURN',     'SRN',  'مرتجع مبيعات'),
                    ('CREDIT_NOTE',      'CRN',  'إشعار دائن لإلغاء فاتورة'),
                    ('CUSTOMER_PAYMENT', 'PAY',  'سند قبض من عميل'),
                    ('PURCHASE_INVOICE', 'PUR',  'فاتورة مشتريات'),
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

            -- An accounting entry type is its own series; its prefix and name follow the type (the guard freezes a used prefix).
            CREATE OR REPLACE FUNCTION fn_entry_type_series() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                INSERT INTO document_series (code, prefix, name_ar) VALUES ('JE.' || NEW.code, NEW.prefix, NEW.name_ar)
                ON CONFLICT (code) DO UPDATE SET prefix = EXCLUDED.prefix, name_ar = EXCLUDED.name_ar;
                RETURN NEW;
            END;
            $$;
            DROP TRIGGER IF EXISTS trg_entry_type_series ON entry_types;
            CREATE TRIGGER trg_entry_type_series AFTER INSERT OR UPDATE OF prefix, name_ar ON entry_types
                FOR EACH ROW EXECUTE FUNCTION fn_entry_type_series();

            CREATE OR REPLACE FUNCTION fn_entry_type_code_fixed() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.code IS DISTINCT FROM OLD.code THEN
                    RAISE EXCEPTION 'An entry type code never changes ("%")', OLD.code;
                END IF;
                RETURN NEW;
            END;
            $$;
            DROP TRIGGER IF EXISTS trg_entry_type_code_fixed ON entry_types;
            CREATE TRIGGER trg_entry_type_code_fixed BEFORE UPDATE OF code ON entry_types
                FOR EACH ROW EXECUTE FUNCTION fn_entry_type_code_fixed();

            CREATE TABLE IF NOT EXISTS deleted_documents (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                series_code text NOT NULL REFERENCES document_series(code),
                serial_no bigint NOT NULL,
                document_number text NOT NULL,
                document_table text NOT NULL,
                document_id uuid NOT NULL,
                status_at_deletion text NOT NULL,
                snapshot jsonb NOT NULL,
                reason text NOT NULL CHECK (length(btrim(reason)) >= 5),
                deleted_by uuid NOT NULL,
                deleted_at timestamptz NOT NULL DEFAULT now(),
                CONSTRAINT ux_deleted_documents_series_serial UNIQUE (series_code, serial_no)
            );
            CREATE INDEX IF NOT EXISTS ix_deleted_documents_deleted_at ON deleted_documents (deleted_at DESC);
            DROP TRIGGER IF EXISTS trg_deleted_documents_immutable ON deleted_documents;
            CREATE TRIGGER trg_deleted_documents_immutable BEFORE UPDATE OR DELETE ON deleted_documents
                FOR EACH ROW EXECUTE FUNCTION fn_audit_log_immutable();

            -- A numbered document leaves only with its record in deleted_documents, written first in the same transaction.
            CREATE OR REPLACE FUNCTION fn_document_delete_guard() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1 FROM deleted_documents d
                    WHERE d.series_code = OLD.series_code AND d.serial_no = OLD.serial_no AND d.document_id = OLD.id) THEN
                    RAISE EXCEPTION 'Document % #% can only be deleted through the recorded deletion (deleted_documents)', OLD.series_code, OLD.serial_no;
                END IF;
                RETURN OLD;
            END;
            $$;

            DROP TRIGGER IF EXISTS trg_invoice_auto_number ON invoices;
            DROP FUNCTION IF EXISTS fn_invoice_auto_number();
            """);

        foreach (var (table, number, series) in NumberedTables)
        {
            migrationBuilder.Sql(NumberingSql(table, number, series));
        }

        migrationBuilder.Sql($"""
            ALTER TABLE entry_types DROP COLUMN IF EXISTS last_number;
            {string.Join("\n", OldSequences.Select(s => $"DROP SEQUENCE IF EXISTS {s};"))}
            """);
    }

    /// <summary>Numbering for one table: backfilled series and serials, then the number, fixed-number and delete-guard triggers. Later migrations reuse it for new numbered tables.</summary>
    internal static string NumberingSql(string table, string number, string series)
    {
        var onNew = series.Replace("{r}", "NEW");
        var onRow = series.Replace("{r}", "x");
        return $"""
            ALTER TABLE {table} ADD COLUMN IF NOT EXISTS series_code text, ADD COLUMN IF NOT EXISTS serial_no bigint;

            -- The backfill only adds the two new columns; the table's own triggers (immutable voided invoices, balance syncs) stay out of it.
            ALTER TABLE {table} DISABLE TRIGGER USER;
            UPDATE {table} x SET series_code = {onRow} WHERE x.series_code IS NULL;
            UPDATE {table} x SET serial_no = r.n
            FROM (SELECT id, row_number() OVER (PARTITION BY series_code ORDER BY created_at, id) AS n FROM {table}) r
            WHERE r.id = x.id AND x.serial_no IS NULL;
            -- A document that never got a printed number (older code paths) gets the one its serial stands for.
            UPDATE {table} x SET {number} = format_document_number(s.prefix, x.serial_no)
            FROM document_series s WHERE s.code = x.series_code AND x.{number} IS NULL;
            ALTER TABLE {table} ENABLE TRIGGER USER;
            UPDATE document_series s SET last_number = m.last, updated_at = now()
            FROM (SELECT series_code, max(serial_no) AS last FROM {table} GROUP BY series_code) m
            WHERE m.series_code = s.code AND m.last > s.last_number;

            ALTER TABLE {table}
                ALTER COLUMN series_code SET NOT NULL,
                ALTER COLUMN serial_no SET NOT NULL,
                ALTER COLUMN {number} SET NOT NULL,
                ADD CONSTRAINT fk_{table}_series FOREIGN KEY (series_code) REFERENCES document_series(code),
                ADD CONSTRAINT ux_{table}_series_serial UNIQUE (series_code, serial_no),
                ADD CONSTRAINT ck_{table}_serial_positive CHECK (serial_no > 0);

            CREATE OR REPLACE FUNCTION fn_{table}_number() RETURNS trigger LANGUAGE plpgsql AS $$
            DECLARE c record;
            BEGIN
                NEW.series_code := {onNew};
                IF NEW.series_code IS NULL THEN
                    RAISE EXCEPTION 'No document series for this {table} row';
                END IF;
                SELECT * INTO c FROM claim_document_number(NEW.series_code);
                NEW.serial_no := c.serial;
                NEW.{number} := c.number;
                RETURN NEW;
            END;
            $$;
            DROP TRIGGER IF EXISTS trg_{table}_number ON {table};
            CREATE TRIGGER trg_{table}_number BEFORE INSERT ON {table} FOR EACH ROW EXECUTE FUNCTION fn_{table}_number();

            CREATE OR REPLACE FUNCTION fn_{table}_number_fixed() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.series_code IS DISTINCT FROM OLD.series_code OR NEW.serial_no IS DISTINCT FROM OLD.serial_no
                   OR NEW.{number} IS DISTINCT FROM OLD.{number} OR ({onNew}) IS DISTINCT FROM OLD.series_code THEN
                    RAISE EXCEPTION 'The number and kind of document {table} % never change', OLD.{number};
                END IF;
                RETURN NEW;
            END;
            $$;
            DROP TRIGGER IF EXISTS trg_{table}_number_fixed ON {table};
            CREATE TRIGGER trg_{table}_number_fixed BEFORE UPDATE ON {table} FOR EACH ROW EXECUTE FUNCTION fn_{table}_number_fixed();

            DROP TRIGGER IF EXISTS trg_{table}_delete_guard ON {table};
            CREATE TRIGGER trg_{table}_delete_guard BEFORE DELETE ON {table} FOR EACH ROW EXECUTE FUNCTION fn_document_delete_guard();
            """;
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        throw new NotSupportedException("Document numbering is not reversible: restore a backup taken before this migration.");
    }
}
