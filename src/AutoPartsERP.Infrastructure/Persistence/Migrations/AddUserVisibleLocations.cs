using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// The locations a user may see: the warehouses assigned to them (user_warehouses) and everything under them. Warehouse lists, documents and
/// movements filter on it for users without <c>inventory:all_warehouses</c>.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000023_AddUserVisibleLocations")]
public sealed class AddUserVisibleLocations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION user_visible_locations(p_user uuid) RETURNS TABLE (location_id uuid) LANGUAGE sql STABLE AS $$
                WITH RECURSIVE tree AS (
                    SELECT uw.warehouse_id AS id FROM user_warehouses uw WHERE uw.user_id = p_user
                    UNION
                    SELECT l.id FROM locations l INNER JOIN tree ON l.parent_id = tree.id
                )
                SELECT id FROM tree;
            $$;
            CREATE INDEX IF NOT EXISTS ix_locations_parent ON locations (parent_id) WHERE parent_id IS NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS user_visible_locations(uuid);");
    }
}
