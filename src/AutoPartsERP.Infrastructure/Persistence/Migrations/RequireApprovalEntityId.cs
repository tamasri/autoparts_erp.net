using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// The EF <c>ApprovalRequest.EntityId</c> property is required, but approval_requests.entity_id was created
/// nullable and MakerCheckerBehavior submits requests with no entity id. A NULL row made every read through EF
/// (the approvals list, approve, reject) throw InvalidCastException. Backfill and make the column NOT NULL with an
/// empty-string default so the two models agree.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000012_RequireApprovalEntityId")]
public sealed class RequireApprovalEntityId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE approval_requests SET entity_id = '' WHERE entity_id IS NULL;
            ALTER TABLE approval_requests ALTER COLUMN entity_id SET DEFAULT '';
            ALTER TABLE approval_requests ALTER COLUMN entity_id SET NOT NULL;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
