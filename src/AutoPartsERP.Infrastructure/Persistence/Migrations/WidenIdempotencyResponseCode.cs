using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// idempotency_keys.response_code held the full serialized response (it is read back as response_json
/// to replay a completed request) but was declared varchar(100). Any response longer than 100 characters
/// made CompleteAsync throw AFTER the business write had committed, so the caller got a 500 for an
/// operation that had actually succeeded. Widening to text is additive and keeps the replay semantics.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000011_WidenIdempotencyResponseCode")]
public sealed class WidenIdempotencyResponseCode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE idempotency_keys ALTER COLUMN response_code TYPE text;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
