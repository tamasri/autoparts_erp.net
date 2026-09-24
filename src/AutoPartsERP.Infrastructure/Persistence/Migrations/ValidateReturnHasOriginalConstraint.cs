using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// <c>ck_invoices_return_has_original</c> (migration 27, AddReturns) was added <c>NOT VALID</c> so it would not scan
/// the table while adding it. That step was never finished: existing rows are still unchecked, only new ones are.
/// <c>VALIDATE CONSTRAINT</c> takes a lock no stronger than a normal write and does not rewrite the table.
/// Only when no old row breaks the rule: a database that still holds returns from before migration 27 (trial data) must
/// keep starting; the demo-data reset validates every pending constraint once those rows are gone.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000029_ValidateReturnHasOriginalConstraint")]
public sealed class ValidateReturnHasOriginalConstraint : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF NOT EXISTS (SELECT 1 FROM invoices WHERE invoice_type = 'RETURN' AND original_invoice_id IS NULL) THEN
                    ALTER TABLE invoices VALIDATE CONSTRAINT ck_invoices_return_has_original;
                END IF;
            END $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Postgres has no "un-validate"; the constraint keeps enforcing itself either way.
    }
}
