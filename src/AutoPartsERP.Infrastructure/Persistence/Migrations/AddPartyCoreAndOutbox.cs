using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20240101000003_AddPartyCoreAndOutbox")]
public sealed class AddPartyCoreAndOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"uuid-ossp\";");
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"pg_trgm\";");

        migrationBuilder.Sql("""
            CREATE SEQUENCE IF NOT EXISTS party_code_seq START 1;

            CREATE TABLE IF NOT EXISTS parties (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                code text NOT NULL UNIQUE,
                display_name text NOT NULL,
                display_name_ar text NOT NULL,
                tax_number text NULL,
                website text NULL,
                notes text NULL,
                is_active boolean NOT NULL DEFAULT true,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL,
                updated_at timestamptz NULL,
                updated_by uuid NULL
            );

            CREATE INDEX IF NOT EXISTS idx_parties_code ON parties(code);
            CREATE INDEX IF NOT EXISTS idx_parties_display_name_trgm ON parties USING GIN(display_name gin_trgm_ops);
            CREATE INDEX IF NOT EXISTS idx_parties_display_name_ar_trgm ON parties USING GIN(display_name_ar gin_trgm_ops);
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS party_type_catalog (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                code text NOT NULL UNIQUE,
                label text NOT NULL,
                label_ar text NOT NULL,
                opens_ar boolean NOT NULL DEFAULT false,
                opens_ap boolean NOT NULL DEFAULT false,
                opens_hr boolean NOT NULL DEFAULT false
            );
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS party_type_assignments (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                party_id uuid NOT NULL REFERENCES parties(id),
                type_code text NOT NULL REFERENCES party_type_catalog(code),
                is_active boolean NOT NULL DEFAULT true,
                requested_by uuid NOT NULL REFERENCES asp_net_users(id),
                approved_by uuid NULL REFERENCES asp_net_users(id),
                approval_id uuid NULL REFERENCES approval_requests(id),
                activated_at timestamptz NULL,
                deactivated_at timestamptz NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                UNIQUE (party_id, type_code)
            );
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS party_contacts (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                party_id uuid NOT NULL REFERENCES parties(id),
                type text NOT NULL,
                value text NOT NULL,
                label text NULL,
                is_primary boolean NOT NULL DEFAULT false,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL
            );
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS party_addresses (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                party_id uuid NOT NULL REFERENCES parties(id),
                type text NOT NULL,
                line1 text NOT NULL,
                line2 text NULL,
                city text NULL,
                region text NULL,
                country text NOT NULL DEFAULT 'SY',
                is_default boolean NOT NULL DEFAULT false,
                created_at timestamptz NOT NULL DEFAULT now()
            );
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS party_notes (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                party_id uuid NOT NULL REFERENCES parties(id),
                content text NOT NULL,
                is_pinned boolean NOT NULL DEFAULT false,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL
            );
            """);

        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF to_regclass('public.customers') IS NOT NULL THEN
                    IF NOT EXISTS (
                        SELECT 1
                        FROM information_schema.columns
                        WHERE table_schema = 'public'
                          AND table_name = 'customers'
                          AND column_name = 'party_id') THEN
                        ALTER TABLE customers ADD COLUMN party_id uuid REFERENCES parties(id);
                    END IF;
                END IF;
            END $$;
            """);

        migrationBuilder.Sql("""
            INSERT INTO parties (id, code, display_name, display_name_ar, is_active, created_at, created_by)
            SELECT
                uuid_generate_v4(),
                'PTY-' || LPAD(ROW_NUMBER() OVER (ORDER BY c.created_at, c.id)::text, 4, '0'),
                c.name,
                c.name,
                c.is_active,
                c.created_at,
                c.created_by
            FROM customers c
            WHERE c.party_id IS NULL;
            """);

        migrationBuilder.Sql("""
            WITH ranked_customers AS (
                SELECT c.id, ROW_NUMBER() OVER (ORDER BY c.created_at, c.id) AS rn
                FROM customers c
                WHERE c.party_id IS NULL
            ),
            ranked_parties AS (
                SELECT p.id, ROW_NUMBER() OVER (ORDER BY p.created_at, p.id) AS rn
                FROM parties p
                WHERE p.code LIKE 'PTY-%'
            )
            UPDATE customers c
            SET party_id = rp.id
            FROM ranked_customers rc
            INNER JOIN ranked_parties rp ON rp.rn = rc.rn
            WHERE c.id = rc.id
              AND c.party_id IS NULL;
            """);

        migrationBuilder.Sql("""
            INSERT INTO party_type_assignments
                (id, party_id, type_code, is_active, requested_by, activated_at, created_at)
            SELECT
                uuid_generate_v4(),
                c.party_id,
                'CUSTOMER',
                TRUE,
                '00000000-0000-0000-0000-000000000001',
                now(),
                now()
            FROM customers c
            WHERE c.party_id IS NOT NULL
              AND NOT EXISTS (
                  SELECT 1
                  FROM party_type_assignments a
                  WHERE a.party_id = c.party_id
                    AND a.type_code = 'CUSTOMER');
            """);

        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM customers WHERE party_id IS NULL) THEN
                    RAISE EXCEPTION 'customers.party_id backfill failed';
                END IF;
                ALTER TABLE customers ALTER COLUMN party_id SET NOT NULL;
            END $$;
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS outbox_messages (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                event_type text NOT NULL,
                aggregate_type text NOT NULL,
                aggregate_id uuid NOT NULL,
                payload_json text NOT NULL,
                occurred_at timestamptz NOT NULL DEFAULT now(),
                processed_at timestamptz NULL,
                processing_error text NULL,
                retry_count int NOT NULL DEFAULT 0,
                correlation_id uuid NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_outbox_unprocessed
                ON outbox_messages(occurred_at ASC) WHERE processed_at IS NULL;
            CREATE INDEX IF NOT EXISTS idx_outbox_aggregate
                ON outbox_messages(aggregate_type, aggregate_id);
            CREATE INDEX IF NOT EXISTS idx_outbox_event_type
                ON outbox_messages(event_type);
            """);

        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION fn_outbox_no_delete()
            RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'outbox_messages cannot be deleted';
            END; $$;

            DROP TRIGGER IF EXISTS trg_outbox_no_delete ON outbox_messages;
            CREATE TRIGGER trg_outbox_no_delete
            BEFORE DELETE ON outbox_messages
            FOR EACH ROW EXECUTE FUNCTION fn_outbox_no_delete();
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS kpi_definitions (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                key text NOT NULL UNIQUE,
                domain text NOT NULL,
                title text NOT NULL,
                title_ar text NOT NULL,
                unit text NOT NULL,
                direction text NOT NULL DEFAULT 'UP',
                description text NULL,
                is_active boolean NOT NULL DEFAULT true,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL
            );

            CREATE TABLE IF NOT EXISTS kpi_thresholds (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                kpi_definition_id uuid NOT NULL REFERENCES kpi_definitions(id),
                warning_value numeric(18,4) NULL,
                critical_value numeric(18,4) NULL,
                effective_from date NOT NULL,
                effective_to date NULL,
                set_by uuid NOT NULL REFERENCES asp_net_users(id),
                created_at timestamptz NOT NULL DEFAULT now()
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

            DROP TRIGGER IF EXISTS trg_party_notes_no_update ON party_notes;
            CREATE TRIGGER trg_party_notes_no_update
            BEFORE UPDATE ON party_notes
            FOR EACH ROW EXECUTE FUNCTION fn_audit_log_immutable();
            """);

        migrationBuilder.Sql("""
            INSERT INTO party_type_catalog
                (id, code, label, label_ar, opens_ar, opens_ap, opens_hr)
            VALUES
                (uuid_generate_v4(), 'CUSTOMER',         'Customer',          'عميل',         TRUE,  FALSE, FALSE),
                (uuid_generate_v4(), 'VENDOR',           'Vendor',            'مورّد',        FALSE, TRUE,  FALSE),
                (uuid_generate_v4(), 'EMPLOYEE',         'Employee',          'موظف',         FALSE, FALSE, TRUE),
                (uuid_generate_v4(), 'DELIVERY_COMPANY', 'Delivery Company',  'شركة توصيل',   FALSE, TRUE,  FALSE),
                (uuid_generate_v4(), 'GOVERNMENT',       'Government Entity', 'جهة حكومية',   FALSE, FALSE, FALSE)
            ON CONFLICT (code) DO NOTHING;
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
