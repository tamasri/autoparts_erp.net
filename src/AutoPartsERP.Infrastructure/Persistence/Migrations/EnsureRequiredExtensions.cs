using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// Guarantees the PostgreSQL extensions the schema depends on are present before any table is created.
/// This migration is ordered first (earliest ID) so every later migration can rely on the extensions.
///
/// - <c>ltree</c>   : required by catalog/category hierarchy columns (<c>path</c>, <c>category_path</c>).
/// - <c>pgvector</c>: required by the AI knowledge-base embedding columns.
///
/// Unlike the previous best-effort attempt in the AI migration (which silently skipped <c>vector</c> when the
/// package was unavailable), this migration treats <c>vector</c> as a hard prerequisite and fails loudly with an
/// actionable message, so a misconfigured database is caught at deploy time instead of at first AI query.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20231231000000_EnsureRequiredExtensions")]
public sealed class EnsureRequiredExtensions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"uuid-ossp\";");
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"pg_trgm\";");
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS \"ltree\";");

        // pgvector is mandatory for the AI knowledge base. If the pgvector package is not installed on the
        // server, fail with a clear message rather than silently degrading.
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM pg_available_extensions WHERE name = 'vector') THEN
                    RAISE EXCEPTION 'Required PostgreSQL extension "vector" (pgvector) is not available on this server. Install the pgvector package (e.g. use the pgvector/pgvector image or "apt install postgresql-16-pgvector") before applying migrations.';
                END IF;
                CREATE EXTENSION IF NOT EXISTS vector;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Extensions are shared infrastructure and may be relied upon by other objects; intentionally not dropped.
    }
}
