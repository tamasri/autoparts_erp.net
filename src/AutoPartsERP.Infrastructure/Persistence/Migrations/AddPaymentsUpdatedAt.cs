using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// AllocatePayment and ReversePayment both run "UPDATE payments SET ... updated_at = now()", but the column was never created,
/// so allocating or reversing any payment failed with a 500.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000014_AddPaymentsUpdatedAt")]
public sealed class AddPaymentsUpdatedAt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE payments ADD COLUMN IF NOT EXISTS updated_at timestamptz;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
