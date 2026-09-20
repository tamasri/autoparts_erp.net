using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// The demo/seed data inserted parties with explicit codes (PTY-0001 ...) without advancing party_code_seq, so the first account
/// created afterwards was given a code that already existed and the request failed with a duplicate key error.
/// The sequence is moved past the highest code in use.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000018_FixPartyCodeSequence")]
public sealed class FixPartyCodeSequence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            SELECT setval('party_code_seq',
                          GREATEST((SELECT COALESCE(MAX(substring(code FROM 5)::int), 0) FROM parties WHERE code ~ '^PTY-[0-9]+$'), 1),
                          (SELECT COALESCE(MAX(substring(code FROM 5)::int), 0) FROM parties WHERE code ~ '^PTY-[0-9]+$') > 0);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
