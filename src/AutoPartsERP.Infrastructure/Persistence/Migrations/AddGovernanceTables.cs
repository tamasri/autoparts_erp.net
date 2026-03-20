using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

public sealed class AddGovernanceTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"uuid-ossp\";");
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"ltree\";");
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"pg_trgm\";");

        migrationBuilder.Sql("CREATE TABLE IF NOT EXISTS audit_logs (id uuid PRIMARY KEY, correlation_id uuid, event_type varchar(100) NOT NULL, module varchar(50) NOT NULL, entity_type varchar(100) NOT NULL, entity_id varchar(100), actor_id uuid NOT NULL, actor_username varchar(100) NOT NULL, action varchar(50) NOT NULL, old_values jsonb, new_values jsonb, diff jsonb, reason_code varchar(100), reason_notes text, ip_address varchar(45), user_agent varchar(500), duration_ms int, status varchar(20) NOT NULL, rejection_reason text, created_at timestamptz NOT NULL);");
        migrationBuilder.Sql("CREATE TABLE IF NOT EXISTS approval_requests (id uuid PRIMARY KEY, correlation_id uuid NOT NULL, request_type varchar(100) NOT NULL, entity_type varchar(100) NOT NULL, entity_id varchar(100), payload_json jsonb NOT NULL, requester_id uuid NOT NULL, requester_notes text, reason_code varchar(100), status varchar(20) NOT NULL DEFAULT 'PENDING', reviewed_by uuid, reviewed_at timestamptz, reviewer_notes text, rejection_reason text, expires_at timestamptz NOT NULL, created_at timestamptz NOT NULL);");
        migrationBuilder.Sql("CREATE TABLE IF NOT EXISTS period_locks (id uuid PRIMARY KEY, period_key varchar(7) NOT NULL, module_code varchar(50) NOT NULL, reason text NOT NULL, is_locked boolean NOT NULL, locked_by_user_id uuid NOT NULL, locked_at_utc timestamptz NOT NULL, unlocked_by_user_id uuid, unlocked_at_utc timestamptz, CONSTRAINT uq_period_key_module UNIQUE(period_key, module_code));");
        migrationBuilder.Sql("CREATE TABLE IF NOT EXISTS reason_codes (id uuid PRIMARY KEY, category varchar(100) NOT NULL, code varchar(100) NOT NULL, description varchar(200) NOT NULL, requires_comment boolean NOT NULL, applies_to varchar(100), is_active boolean NOT NULL, created_at_utc timestamptz NOT NULL, updated_at_utc timestamptz, CONSTRAINT uq_reason_codes_code UNIQUE(code));");
        migrationBuilder.Sql("CREATE TABLE IF NOT EXISTS idempotency_keys (id uuid PRIMARY KEY, key varchar(200) NOT NULL, scope varchar(200) NOT NULL, request_hash varchar(128) NOT NULL, expires_at_utc timestamptz NOT NULL, is_completed boolean NOT NULL, resource_id varchar(200), response_code varchar(100), completed_at_utc timestamptz, CONSTRAINT uq_idempotency_key_scope UNIQUE(key, scope));");
        migrationBuilder.Sql("CREATE TABLE IF NOT EXISTS rejection_attempts (id uuid PRIMARY KEY, correlation_id uuid NOT NULL, user_id uuid NOT NULL, username varchar(100) NOT NULL, endpoint varchar(200) NOT NULL, permission_required varchar(200), reason varchar(500) NOT NULL, ip_address varchar(45), created_at timestamptz NOT NULL);");

        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_audit_logs_created_at_desc ON audit_logs(created_at DESC);");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_approval_requests_expires_at ON approval_requests(expires_at);");
        migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS ix_idempotency_keys_expires_at ON idempotency_keys(expires_at_utc);");

        migrationBuilder.Sql("INSERT INTO \"AspNetRoles\" (\"Id\",\"Name\",\"NormalizedName\",\"ConcurrencyStamp\") VALUES ('10000000-0000-0000-0000-000000000001','SUPER_ADMIN','SUPER_ADMIN','seed-super-admin') ON CONFLICT (\"Id\") DO NOTHING;");

        foreach (var permission in PermissionCodes.All)
        {
            var safePermission = permission.Replace("'", "''");
            migrationBuilder.Sql($"INSERT INTO \"AspNetRoleClaims\" (\"RoleId\",\"ClaimType\",\"ClaimValue\") SELECT '10000000-0000-0000-0000-000000000001','permission','{safePermission}' WHERE NOT EXISTS (SELECT 1 FROM \"AspNetRoleClaims\" WHERE \"RoleId\"='10000000-0000-0000-0000-000000000001' AND \"ClaimType\"='permission' AND \"ClaimValue\"='{safePermission}');");
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}