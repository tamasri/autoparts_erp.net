namespace AutoPartsERP.Infrastructure.Persistence;

/// <summary>
/// Reference data every installation needs (production included): the reason codes the void / price-override / stock-adjustment
/// dialogs offer and the KPI definitions. Idempotent (ON CONFLICT DO NOTHING), so an administrator's own edits are never overwritten.
/// Business data for trying the system out is in <see cref="DemoDataSeeder"/>, which runs in Development only.
/// </summary>
public static class ReferenceDataSeeder
{
    private static readonly Guid SystemUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var connectionString = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var adminId = await connection.ExecuteScalarAsync<Guid?>("SELECT id FROM asp_net_users WHERE user_name = 'admin' LIMIT 1;") ?? SystemUserId;

        await connection.ExecuteAsync(
            """
            INSERT INTO reason_codes (
                id, category, code, description, requires_comment, applies_to, is_active, created_at_utc, updated_at_utc)
            VALUES
                (uuid_generate_v4(), 'PRICE_OVERRIDE', 'COMP-MATCH',  'مطابقة سعر المنافس', TRUE,  'MEDIUM', TRUE, now(), NULL),
                (uuid_generate_v4(), 'PRICE_OVERRIDE', 'LOYAL-CUST',  'عميل مخلص',           FALSE, 'LOW',    TRUE, now(), NULL),
                (uuid_generate_v4(), 'VOID_INVOICE',   'ENTRY-ERR',   'خطأ في الإدخال',      TRUE,  'HIGH',   TRUE, now(), NULL),
                (uuid_generate_v4(), 'VOID_INVOICE',   'CUST-CANCEL', 'إلغاء العميل',         TRUE,  'MEDIUM', TRUE, now(), NULL),
                (uuid_generate_v4(), 'STOCK_ADJUST',   'DAMAGE',      'تلف',                  TRUE,  'HIGH',   TRUE, now(), NULL),
                (uuid_generate_v4(), 'STOCK_ADJUST',   'COUNT-DIFF',  'فرق جرد',              TRUE,  'MEDIUM', TRUE, now(), NULL)
            ON CONFLICT (code) DO NOTHING;
            """);

        await connection.ExecuteAsync(
            """
            INSERT INTO kpi_definitions (
                id, key, domain, title, title_ar, unit, direction, description, is_active, created_at, created_by)
            VALUES
                (uuid_generate_v4(), 'sales.total_invoiced_today', 'SALES', 'Today Invoiced', 'مبيعات اليوم', 'SYP', 'UP', NULL, TRUE, now(), @CreatedBy),
                (uuid_generate_v4(), 'sales.active_workshops', 'SALES', 'Active Workshops', 'ورش نشطة', 'COUNT', 'UP', NULL, TRUE, now(), @CreatedBy),
                (uuid_generate_v4(), 'sales.overdue_amount', 'SALES', 'Overdue Receivables', 'متأخرات التحصيل', 'SYP', 'DOWN', NULL, TRUE, now(), @CreatedBy),
                (uuid_generate_v4(), 'inventory.items_below_rop', 'INVENTORY', 'Items Below ROP', 'مواد تحت الحد', 'COUNT', 'DOWN', NULL, TRUE, now(), @CreatedBy),
                (uuid_generate_v4(), 'inventory.stockout_count', 'INVENTORY', 'Stockout Count', 'نفاد المخزون', 'COUNT', 'DOWN', NULL, TRUE, now(), @CreatedBy),
                (uuid_generate_v4(), 'finance.outstanding_ar', 'FINANCE', 'Outstanding AR', 'ذمم مدينة', 'SYP', 'DOWN', NULL, TRUE, now(), @CreatedBy)
            ON CONFLICT (key) DO NOTHING;
            """,
            new { CreatedBy = adminId });
    }
}
