using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// Reconciles the <c>approval_requests</c> table (created via raw SQL in
/// <see cref="AddGovernanceTables"/>) with the columns the EF <c>ApprovalRequest</c> entity has
/// always expected (<c>action_code</c>, <c>requested_by_user_id</c>, <c>reason</c>,
/// <c>required_approvals</c>, <c>requested_at_utc</c>, <c>completed_at_utc</c>, <c>created_at_utc</c>,
/// <c>updated_at_utc</c>) and creates the missing <c>approval_decisions</c> table backing
/// <c>ApprovalRequest.Decisions</c>.
///
/// Discovered while wiring <c>GovernanceService.ApproveApprovalAsync</c> to actually replay the
/// approved command: without this migration, <c>_dbContext.ApprovalRequests</c> queries/saves fail
/// with "column does not exist" (42703), because the EF model and the hand-written table never matched.
/// This affected the Approvals feature (list/approve/reject) independent of the maker-checker fix.
/// Additive and idempotent — safe to apply on top of existing data.
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000006_FixApprovalGovernanceSchema")]
public sealed class FixApprovalGovernanceSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE approval_requests
                ADD COLUMN IF NOT EXISTS action_code varchar(100) NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS requested_by_user_id uuid,
                ADD COLUMN IF NOT EXISTS reason text NOT NULL DEFAULT '',
                ADD COLUMN IF NOT EXISTS required_approvals int NOT NULL DEFAULT 1,
                ADD COLUMN IF NOT EXISTS requested_at_utc timestamptz,
                ADD COLUMN IF NOT EXISTS completed_at_utc timestamptz,
                ADD COLUMN IF NOT EXISTS created_at_utc timestamptz,
                ADD COLUMN IF NOT EXISTS updated_at_utc timestamptz;
            """);

        // Backfill existing rows (inserted by ApprovalService.CreatePendingApprovalAsync's raw SQL,
        // which only ever populated request_type/entity_type/entity_id/payload_json/requester_*).
        migrationBuilder.Sql("""
            UPDATE approval_requests SET requested_by_user_id = requester_id WHERE requested_by_user_id IS NULL;
            UPDATE approval_requests SET action_code = request_type WHERE action_code = '';
            UPDATE approval_requests SET reason = COALESCE(requester_notes, '') WHERE reason = '';
            UPDATE approval_requests SET requested_at_utc = created_at WHERE requested_at_utc IS NULL;
            UPDATE approval_requests SET created_at_utc = created_at WHERE created_at_utc IS NULL;
            """);

        migrationBuilder.Sql("""
            ALTER TABLE approval_requests
                ALTER COLUMN requested_by_user_id SET NOT NULL,
                ALTER COLUMN requested_at_utc SET NOT NULL,
                ALTER COLUMN created_at_utc SET NOT NULL;
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS approval_decisions (
                id uuid PRIMARY KEY,
                created_at_utc timestamptz NOT NULL DEFAULT now(),
                updated_at_utc timestamptz,
                approval_request_id uuid NOT NULL REFERENCES approval_requests(id) ON DELETE CASCADE,
                reviewer_user_id uuid NOT NULL,
                status varchar(20) NOT NULL,
                comment text,
                reviewed_at_utc timestamptz NOT NULL
            );
            """);

        migrationBuilder.Sql(
            "CREATE INDEX IF NOT EXISTS ix_approval_decisions_approval_request_id ON approval_decisions(approval_request_id);");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Additive/backfill migration — intentionally not reversed (would silently drop governance history).
    }
}
