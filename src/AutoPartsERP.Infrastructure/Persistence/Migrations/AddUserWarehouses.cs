using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// Which warehouses (top-level locations) each user works in and manages, set by an administrator. A manager approves transfers into or out of
/// their warehouses. A held transfer keeps the warehouses it touches in approval_requests.scope_warehouse_ids, so it can be routed to their managers.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000020_AddUserWarehouses")]
public sealed class AddUserWarehouses : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE user_warehouses (
                user_id uuid NOT NULL REFERENCES asp_net_users(id) ON DELETE CASCADE,
                warehouse_id uuid NOT NULL REFERENCES locations(id) ON DELETE CASCADE,
                is_manager boolean NOT NULL DEFAULT FALSE,
                assigned_by uuid,
                assigned_at timestamptz NOT NULL DEFAULT now(),
                PRIMARY KEY (user_id, warehouse_id)
            );
            CREATE INDEX ix_user_warehouses_managers ON user_warehouses (warehouse_id) WHERE is_manager;

            ALTER TABLE approval_requests ADD COLUMN IF NOT EXISTS scope_warehouse_ids uuid[];
            CREATE INDEX IF NOT EXISTS ix_approval_requests_scope ON approval_requests USING gin (scope_warehouse_ids);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS ix_approval_requests_scope;
            ALTER TABLE approval_requests DROP COLUMN IF EXISTS scope_warehouse_ids;
            DROP TABLE IF EXISTS user_warehouses;
            """);
    }
}
