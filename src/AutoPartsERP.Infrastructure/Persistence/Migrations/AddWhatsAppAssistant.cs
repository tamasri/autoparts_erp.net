using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AutoPartsERP.Infrastructure.Persistence.Migrations;

/// <summary>
/// WhatsApp assistant:
/// <list type="bullet">
/// <item><c>ar_norm(text)</c> — one definition of "the same Arabic text" (hamza forms of alef → ا, ة → ه, ى → ي, ؤ → و, ئ → ي, no
/// tashkeel or tatweel, Arabic-Indic digits → 0-9, lower case, punctuation → space). Applied in SQL to both the stored names and
/// the searched text, so "شركه الافق" and "شركة الأُفُق" compare equal; trigram indexes cover the normalized names.</item>
/// <item><c>assistant_links</c> — the phone numbers allowed to talk to the assistant, each bound to one ERP user. A link is active
/// only after the phone itself sent the one-time code (proves possession); the WhatsApp id seen at that moment is stored.</item>
/// <item>Feature flag <c>WHATSAPP_ASSISTANT</c> (off switch; <c>assistant:use</c> required).</item>
/// </list>
/// </summary>
[DbContext(typeof(AppDbContext))]
[Migration("20240101000024_AddWhatsAppAssistant")]
public sealed class AddWhatsAppAssistant : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE OR REPLACE FUNCTION ar_norm(t text) RETURNS text LANGUAGE sql IMMUTABLE PARALLEL SAFE AS $$
                -- Joiners inside codes (5W-30, 04152/YZZA1) are dropped so "5w30" matches; other punctuation separates words.
                SELECT btrim(regexp_replace(regexp_replace(regexp_replace(
                    translate(lower(coalesce(t, '')),
                              'أإآٱةىؤئ٠١٢٣٤٥٦٧٨٩ـًٌٍَُِّْٰ',
                              'ااااهيوي0123456789'),
                    '[-_./\\]', '', 'g'),
                    '[^0-9a-zء-ي ]', ' ', 'g'), '\s+', ' ', 'g'));
            $$;

            CREATE INDEX IF NOT EXISTS ix_customers_name_arnorm_trgm ON customers USING gin (ar_norm(name) gin_trgm_ops);
            CREATE INDEX IF NOT EXISTS ix_skus_name_arnorm_trgm ON skus USING gin (ar_norm(name) gin_trgm_ops);
            CREATE INDEX IF NOT EXISTS ix_skus_name_ar_arnorm_trgm ON skus USING gin (ar_norm(name_ar) gin_trgm_ops);

            CREATE TABLE IF NOT EXISTS assistant_links (
                id uuid PRIMARY KEY DEFAULT uuid_generate_v4(),
                user_id uuid NOT NULL REFERENCES asp_net_users (id),
                phone text NOT NULL CHECK (phone ~ '^[0-9]{8,15}$'),
                whatsapp_id text,
                status text NOT NULL DEFAULT 'PENDING' CHECK (status IN ('PENDING', 'ACTIVE', 'REVOKED')),
                link_code_hash text,
                link_code_expires_at timestamptz,
                failed_attempts int NOT NULL DEFAULT 0,
                created_at timestamptz NOT NULL DEFAULT now(),
                created_by uuid NOT NULL,
                verified_at timestamptz,
                revoked_at timestamptz,
                revoked_by uuid,
                last_used_at timestamptz
            );
            CREATE UNIQUE INDEX IF NOT EXISTS ux_assistant_links_live_phone ON assistant_links (phone) WHERE status <> 'REVOKED';
            CREATE INDEX IF NOT EXISTS ix_assistant_links_whatsapp_id ON assistant_links (whatsapp_id) WHERE status = 'ACTIVE';

            INSERT INTO ai_feature_flags (feature_code, label, model_name, is_enabled, required_permission, allowed_roles, allow_writes_to_core_data, created_by)
            VALUES ('WHATSAPP_ASSISTANT', 'مساعد واتساب', 'llama-3.3-70b-versatile', true, 'assistant:use', '{}', false, '00000000-0000-0000-0000-000000000001')
            ON CONFLICT (feature_code) DO NOTHING;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM ai_feature_flags WHERE feature_code = 'WHATSAPP_ASSISTANT';
            DROP TABLE IF EXISTS assistant_links;
            DROP INDEX IF EXISTS ix_skus_name_ar_arnorm_trgm;
            DROP INDEX IF EXISTS ix_skus_name_arnorm_trgm;
            DROP INDEX IF EXISTS ix_customers_name_arnorm_trgm;
            DROP FUNCTION IF EXISTS ar_norm(text);
            """);
    }
}
