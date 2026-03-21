using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20240101000005_AddAiFoundationTables")]
public sealed class AddAiFoundationTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (SELECT 1 FROM pg_available_extensions WHERE name = 'vector') THEN
                    CREATE EXTENSION IF NOT EXISTS vector;
                END IF;
            END $$;
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS ai_feature_flags (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                feature_code text NOT NULL UNIQUE,
                label text NOT NULL,
                model_name text NOT NULL,
                is_enabled boolean NOT NULL DEFAULT true,
                required_permission text NOT NULL,
                allowed_roles text[] NOT NULL DEFAULT '{}'::text[],
                allow_writes_to_core_data boolean NOT NULL DEFAULT false,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL,
                updated_at timestamptz NULL,
                updated_by uuid NULL
            );
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS ai_sessions (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                user_id uuid NOT NULL REFERENCES asp_net_users(id),
                feature_code text NOT NULL,
                title text NULL,
                context_json jsonb NOT NULL DEFAULT '{}'::jsonb,
                is_active boolean NOT NULL DEFAULT true,
                last_interaction_at timestamptz NOT NULL DEFAULT now(),
                expires_at timestamptz NOT NULL,
                created_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS idx_ai_sessions_user_feature ON ai_sessions(user_id, feature_code);
            CREATE INDEX IF NOT EXISTS idx_ai_sessions_expires_at ON ai_sessions(expires_at);
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS ai_prompt_logs (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                session_id uuid NULL REFERENCES ai_sessions(id),
                user_id uuid NOT NULL REFERENCES asp_net_users(id),
                feature_code text NOT NULL,
                model_name text NOT NULL,
                prompt_text text NOT NULL,
                response_text text NOT NULL,
                success boolean NOT NULL,
                prompt_tokens int NULL,
                completion_tokens int NULL,
                latency_ms int NOT NULL DEFAULT 0,
                tool_calls_json jsonb NULL,
                created_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS idx_ai_prompt_logs_feature_time ON ai_prompt_logs(feature_code, created_at DESC);
            CREATE INDEX IF NOT EXISTS idx_ai_prompt_logs_user_time ON ai_prompt_logs(user_id, created_at DESC);
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS ai_suggestions (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                session_id uuid NULL REFERENCES ai_sessions(id),
                user_id uuid NOT NULL REFERENCES asp_net_users(id),
                feature_code text NOT NULL,
                title text NOT NULL,
                content text NOT NULL,
                suggested_action_code text NOT NULL,
                payload_json jsonb NOT NULL DEFAULT '{}'::jsonb,
                status text NOT NULL DEFAULT 'PENDING' CHECK (status IN ('PENDING','ACCEPTED','REJECTED','EXPIRED')),
                expires_at timestamptz NOT NULL,
                reviewed_by uuid NULL REFERENCES asp_net_users(id),
                reviewed_at timestamptz NULL,
                review_notes text NULL,
                created_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS idx_ai_suggestions_user_status ON ai_suggestions(user_id, status);
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS ai_task_runs (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                task_code text NOT NULL,
                status text NOT NULL CHECK (status IN ('PENDING','RUNNING','COMPLETED','FAILED','SKIPPED')),
                started_at timestamptz NOT NULL,
                completed_at timestamptz NULL,
                output_summary text NULL,
                error_message text NULL,
                created_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS idx_ai_task_runs_task_time ON ai_task_runs(task_code, started_at DESC);
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS ai_feedback (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                prompt_log_id uuid NOT NULL REFERENCES ai_prompt_logs(id),
                user_id uuid NOT NULL REFERENCES asp_net_users(id),
                rating int NOT NULL CHECK (rating BETWEEN 1 AND 5),
                was_helpful boolean NOT NULL,
                correction text NULL,
                created_at timestamptz NOT NULL DEFAULT now()
            );
            CREATE INDEX IF NOT EXISTS idx_ai_feedback_prompt_log ON ai_feedback(prompt_log_id);
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS ai_documents (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                title text NOT NULL,
                source_type text NOT NULL,
                source_path text NOT NULL UNIQUE,
                content_text text NOT NULL,
                language_code text NOT NULL DEFAULT 'ar',
                content_embedding double precision[] NULL,
                is_active boolean NOT NULL DEFAULT true,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_ai_documents_active ON ai_documents(is_active);
            """);

        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS ai_scheduled_tasks (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                task_code text NOT NULL UNIQUE,
                cron_expression text NOT NULL,
                is_enabled boolean NOT NULL DEFAULT true,
                last_run_at timestamptz NULL,
                next_run_at timestamptz NULL,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL,
                updated_at timestamptz NULL,
                updated_by uuid NULL
            );
            """);

        migrationBuilder.Sql("""
            DROP TRIGGER IF EXISTS trg_ai_prompt_logs_immutable ON ai_prompt_logs;
            CREATE TRIGGER trg_ai_prompt_logs_immutable
                BEFORE UPDATE OR DELETE ON ai_prompt_logs
                FOR EACH ROW EXECUTE FUNCTION fn_audit_log_immutable();
            """);

        migrationBuilder.Sql("""
            INSERT INTO ai_feature_flags
                (id, feature_code, label, model_name, is_enabled, required_permission, allowed_roles, allow_writes_to_core_data, created_by)
            VALUES
                (uuid_generate_v4(), 'SIDEBAR_ADVISOR', 'Sidebar Advisor', 'llama3.2:3b', true, 'ai:chat', ARRAY['ADMIN','ACCOUNTANT']::text[], false, '00000000-0000-0000-0000-000000000001'),
                (uuid_generate_v4(), 'ACCOUNTING_CHECK', 'Accounting Check', 'llama3.1:8b', true, 'ai:task_runs:read', ARRAY['ADMIN','ACCOUNTANT']::text[], false, '00000000-0000-0000-0000-000000000001'),
                (uuid_generate_v4(), 'ITEM_ASSISTANT', 'Item Assistant', 'llama3.2:3b', true, 'items:read', ARRAY['ADMIN','WAREHOUSE','SALES']::text[], false, '00000000-0000-0000-0000-000000000001')
            ON CONFLICT (feature_code) DO NOTHING;
            """);

        migrationBuilder.Sql("""
            INSERT INTO ai_scheduled_tasks
                (id, task_code, cron_expression, is_enabled, created_by)
            VALUES
                (uuid_generate_v4(), 'ACCOUNTING_CHECK', '0 4 * * *', true, '00000000-0000-0000-0000-000000000001'),
                (uuid_generate_v4(), 'LOW_STOCK_SUMMARY', '0 */6 * * *', true, '00000000-0000-0000-0000-000000000001')
            ON CONFLICT (task_code) DO NOTHING;
            """);

        foreach (var permission in PermissionCodes.All)
        {
            var safePermission = permission.Replace("'", "''", StringComparison.Ordinal);
            migrationBuilder.Sql($"""
                INSERT INTO asp_net_role_claims (role_id,claim_type,claim_value)
                SELECT '10000000-0000-0000-0000-000000000001','permission','{safePermission}'
                WHERE NOT EXISTS (
                    SELECT 1 FROM asp_net_role_claims
                    WHERE role_id = '10000000-0000-0000-0000-000000000001'
                      AND claim_type = 'permission'
                      AND claim_value = '{safePermission}');
                """);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
    }
}
