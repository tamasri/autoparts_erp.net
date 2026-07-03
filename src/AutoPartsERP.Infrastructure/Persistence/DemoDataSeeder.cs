namespace AutoPartsERP.Infrastructure.Persistence;

public static class DemoDataSeeder
{
    private static readonly Guid SystemUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var connectionString = dbContext.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();

        var demoExists = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM customers WHERE code = 'WS-0001';",
            transaction: tx);

        if (demoExists > 0)
        {
            await tx.CommitAsync();
            return;
        }

        var adminId = await connection.ExecuteScalarAsync<Guid?>(
            "SELECT id FROM asp_net_users WHERE user_name = 'admin' LIMIT 1;",
            transaction: tx) ?? SystemUserId;

        var now = DateTime.UtcNow;
        var today = now.Date;
        var yesterday = today.AddDays(-1);
        var twoDaysAgo = today.AddDays(-2);

        await connection.ExecuteAsync(
            """
            INSERT INTO fx_rates (id, rate_date, currency_from, currency_to, buy_rate, sell_rate, is_active, created_at, created_by)
            VALUES
                ('21000000-0000-0000-0000-000000000001', @Today, 'USD', 'SYP', 13500, 13450, TRUE, now(), @CreatedBy),
                ('21000000-0000-0000-0000-000000000002', @Yesterday, 'USD', 'SYP', 13480, 13430, TRUE, now(), @CreatedBy),
                ('21000000-0000-0000-0000-000000000003', @TwoDaysAgo, 'USD', 'SYP', 13460, 13410, TRUE, now(), @CreatedBy)
            ON CONFLICT (rate_date, currency_from, currency_to) DO NOTHING;
            """,
            new { Today = today, Yesterday = yesterday, TwoDaysAgo = twoDaysAgo, CreatedBy = adminId },
            tx);

        await connection.ExecuteAsync(
            """
            INSERT INTO categories (id, path, name, name_ar, parent_id, depth, is_active, created_at, created_by)
            VALUES
                ('22000000-0000-0000-0000-000000000001', 'parts', 'قطع غيار', 'قطع غيار', NULL, 0, TRUE, now(), @CreatedBy),
                ('22000000-0000-0000-0000-000000000002', 'parts.engine', 'محرك', 'محرك', '22000000-0000-0000-0000-000000000001', 1, TRUE, now(), @CreatedBy),
                ('22000000-0000-0000-0000-000000000003', 'parts.engine.filters', 'فلاتر', 'فلاتر', '22000000-0000-0000-0000-000000000002', 2, TRUE, now(), @CreatedBy),
                ('22000000-0000-0000-0000-000000000004', 'parts.engine.belts', 'سيور', 'سيور', '22000000-0000-0000-0000-000000000002', 2, TRUE, now(), @CreatedBy),
                ('22000000-0000-0000-0000-000000000005', 'parts.brakes', 'فرامل', 'فرامل', '22000000-0000-0000-0000-000000000001', 1, TRUE, now(), @CreatedBy),
                ('22000000-0000-0000-0000-000000000006', 'parts.suspension', 'تعليق', 'تعليق', '22000000-0000-0000-0000-000000000001', 1, TRUE, now(), @CreatedBy),
                ('22000000-0000-0000-0000-000000000007', 'parts.electrical', 'كهرباء', 'كهرباء', '22000000-0000-0000-0000-000000000001', 1, TRUE, now(), @CreatedBy),
                ('22000000-0000-0000-0000-000000000008', 'parts.body', 'هيكل', 'هيكل', '22000000-0000-0000-0000-000000000001', 1, TRUE, now(), @CreatedBy)
            ON CONFLICT (path) DO NOTHING;
            """,
            new { CreatedBy = adminId },
            tx);

        await connection.ExecuteAsync(
            """
            INSERT INTO locations (id, code, name, type, is_active, created_at, created_by)
            VALUES
                ('23000000-0000-0000-0000-000000000001', 'MAIN',   'المستودع الرئيسي', 'WAREHOUSE', TRUE, now(), @CreatedBy),
                ('23000000-0000-0000-0000-000000000002', 'WH2',    'مستودع الفرع',     'WAREHOUSE', TRUE, now(), @CreatedBy),
                ('23000000-0000-0000-0000-000000000003', 'VAN1',   'فان المبيعات 1',   'VEHICLE',   TRUE, now(), @CreatedBy),
                ('23000000-0000-0000-0000-000000000004', 'RETURN', 'منطقة الإرجاع',    'RETURN',    TRUE, now(), @CreatedBy)
            ON CONFLICT (code) DO NOTHING;
            """,
            new { CreatedBy = adminId },
            tx);

        await connection.ExecuteAsync(
            """
            INSERT INTO parties (id, code, display_name, display_name_ar, is_active, created_at, created_by)
            VALUES
                ('24000000-0000-0000-0000-000000000001', 'PTY-0001', 'ورشة أبو علي',          'ورشة أبو علي للسيارات', TRUE, now(), @CreatedBy),
                ('24000000-0000-0000-0000-000000000002', 'PTY-0002', 'ورشة الأمل',            'ورشة الأمل', TRUE, now(), @CreatedBy),
                ('24000000-0000-0000-0000-000000000003', 'PTY-0003', 'ورشة الحارثي',          'ورشة الحارثي', TRUE, now(), @CreatedBy),
                ('24000000-0000-0000-0000-000000000004', 'PTY-0004', 'معرض الوفاء للتجزئة',   'معرض الوفاء للتجزئة', TRUE, now(), @CreatedBy),
                ('24000000-0000-0000-0000-000000000005', 'PTY-0005', 'ورشة النجوم',           'ورشة النجوم', TRUE, now(), @CreatedBy),
                ('24000000-0000-0000-0000-000000000006', 'PTY-0006', 'تاجر الجملة الشامي',    'تاجر الجملة الشامي', TRUE, now(), @CreatedBy)
            ON CONFLICT (code) DO NOTHING;
            """,
            new { CreatedBy = adminId },
            tx);

        await connection.ExecuteAsync(
            """
            INSERT INTO party_type_assignments (id, party_id, type_code, is_active, requested_by, approved_by, activated_at, created_at)
            VALUES
                ('24010000-0000-0000-0000-000000000001', '24000000-0000-0000-0000-000000000001', 'CUSTOMER', TRUE, @CreatedBy, @CreatedBy, now(), now()),
                ('24010000-0000-0000-0000-000000000002', '24000000-0000-0000-0000-000000000002', 'CUSTOMER', TRUE, @CreatedBy, @CreatedBy, now(), now()),
                ('24010000-0000-0000-0000-000000000003', '24000000-0000-0000-0000-000000000003', 'CUSTOMER', TRUE, @CreatedBy, @CreatedBy, now(), now()),
                ('24010000-0000-0000-0000-000000000004', '24000000-0000-0000-0000-000000000004', 'CUSTOMER', TRUE, @CreatedBy, @CreatedBy, now(), now()),
                ('24010000-0000-0000-0000-000000000005', '24000000-0000-0000-0000-000000000005', 'CUSTOMER', TRUE, @CreatedBy, @CreatedBy, now(), now()),
                ('24010000-0000-0000-0000-000000000006', '24000000-0000-0000-0000-000000000006', 'CUSTOMER', TRUE, @CreatedBy, @CreatedBy, now(), now())
            ON CONFLICT (party_id, type_code) DO NOTHING;
            """,
            new { CreatedBy = adminId },
            tx);

        await connection.ExecuteAsync(
            """
            INSERT INTO customers (
                id, party_id, code, name, type, phone, city,
                credit_limit_syp, credit_limit_usd, payment_terms_days,
                is_active, assigned_sales_rep, created_at, created_by)
            VALUES
                ('25000000-0000-0000-0000-000000000001', '24000000-0000-0000-0000-000000000001', 'WS-0001', 'ورشة أبو علي للسيارات', 'WORKSHOP',  '0912345678', 'دمشق',    2000000, 500, 30, TRUE, @CreatedBy, now(), @CreatedBy),
                ('25000000-0000-0000-0000-000000000002', '24000000-0000-0000-0000-000000000002', 'WS-0002', 'ورشة الأمل',             'WORKSHOP',  '0923456789', 'حلب',     1500000, 300, 30, TRUE, @CreatedBy, now(), @CreatedBy),
                ('25000000-0000-0000-0000-000000000003', '24000000-0000-0000-0000-000000000003', 'WS-0003', 'ورشة الحارثي',           'WORKSHOP',  '0934567890', 'دمشق',    3000000, 800, 30, TRUE, @CreatedBy, now(), @CreatedBy),
                ('25000000-0000-0000-0000-000000000004', '24000000-0000-0000-0000-000000000004', 'RT-0001', 'معرض الوفاء للتجزئة',    'RETAIL',    '0945678901', 'حمص',      500000, 100, 30, TRUE, @CreatedBy, now(), @CreatedBy),
                ('25000000-0000-0000-0000-000000000005', '24000000-0000-0000-0000-000000000005', 'WS-0004', 'ورشة النجوم',            'WORKSHOP',  '0956789012', 'اللاذقية', 1000000, 200, 30, TRUE, @CreatedBy, now(), @CreatedBy),
                ('25000000-0000-0000-0000-000000000006', '24000000-0000-0000-0000-000000000006', 'WH-0001', 'تاجر الجملة الشامي',     'WHOLESALE', '0967890123', 'دمشق',   10000000, 2000, 30, TRUE, @CreatedBy, now(), @CreatedBy)
            ON CONFLICT (code) DO NOTHING;
            """,
            new { CreatedBy = adminId },
            tx);

        await connection.ExecuteAsync(
            """
            INSERT INTO skus (
                id, code, name, name_ar, category_id,
                selling_price_syp, selling_price_usd, min_selling_price_syp, min_selling_price_usd,
                cost_price_syp, cost_price_usd, is_batch_tracked, has_warranty, warranty_months,
                is_active, attributes, tags, created_at, created_by)
            VALUES
                ('26000000-0000-0000-0000-000000000001', 'OIL-FILT-TOY-001', 'Oil Filter Toyota Corolla', 'فلتر زيت تويوتا كورولا', '22000000-0000-0000-0000-000000000003', 15000, 1.10, 13000, 0.95,  9000, 0.65, FALSE, FALSE, 0, TRUE, '{}'::jsonb, '{}'::text[], now(), @CreatedBy),
                ('26000000-0000-0000-0000-000000000002', 'OIL-FILT-KIA-001', 'Oil Filter Kia Sportage',  'فلتر زيت كيا سبورتاج',   '22000000-0000-0000-0000-000000000003', 18000, 1.30, 16000, 1.10, 11000, 0.80, FALSE, FALSE, 0, TRUE, '{}'::jsonb, '{}'::text[], now(), @CreatedBy),
                ('26000000-0000-0000-0000-000000000003', 'AIR-FILT-TOY-001', 'Air Filter Toyota',         'فلتر هواء تويوتا',       '22000000-0000-0000-0000-000000000003', 22000, 1.60, 20000, 1.45, 13000, 0.95, FALSE, FALSE, 0, TRUE, '{}'::jsonb, '{}'::text[], now(), @CreatedBy),
                ('26000000-0000-0000-0000-000000000004', 'BRAKE-PAD-TOY-F',  'Brake Pads Toyota Front',   'تيل فرامل تويوتا أمامي', '22000000-0000-0000-0000-000000000005', 85000, 6.20, 76000, 5.50, 52000, 3.80, TRUE,  FALSE, 0, TRUE, '{}'::jsonb, '{}'::text[], now(), @CreatedBy),
                ('26000000-0000-0000-0000-000000000005', 'SPARK-NGK-BP6ES',  'Spark Plug NGK BP6ES',      'بوجيه NGK BP6ES',        '22000000-0000-0000-0000-000000000007', 12000, 0.88, 11000, 0.80,  7000, 0.51, FALSE, FALSE, 0, TRUE, '{}'::jsonb, '{}'::text[], now(), @CreatedBy),
                ('26000000-0000-0000-0000-000000000006', 'BELT-TIMING-TOY',  'Timing Belt Toyota',        'سير تايمنق تويوتا',      '22000000-0000-0000-0000-000000000004', 95000, 6.90, 86000, 6.20, 58000, 4.20, FALSE, FALSE, 0, TRUE, '{}'::jsonb, '{}'::text[], now(), @CreatedBy),
                ('26000000-0000-0000-0000-000000000007', 'SHOCK-FRONT-TOY',  'Shock Absorber Front Toyota','مساعد أمامي تويوتا',    '22000000-0000-0000-0000-000000000006',185000,13.50,166000,12.00,115000, 8.40, FALSE, FALSE, 0, TRUE, '{}'::jsonb, '{}'::text[], now(), @CreatedBy),
                ('26000000-0000-0000-0000-000000000008', 'OIL-FILT-MITS-001','Oil Filter Mitsubishi',     'فلتر زيت ميتسوبيشي',     '22000000-0000-0000-0000-000000000003', 16000, 1.16, 14000, 1.00,  9500, 0.69, FALSE, FALSE, 0, TRUE, '{}'::jsonb, '{}'::text[], now(), @CreatedBy),
                ('26000000-0000-0000-0000-000000000009', 'BATTERY-12V-60AH', 'Battery 12V 60Ah',          'بطارية 12 فولت 60 أمبير','22000000-0000-0000-0000-000000000007',320000,23.00,288000,20.00,200000,14.50, FALSE, FALSE, 0, TRUE, '{}'::jsonb, '{}'::text[], now(), @CreatedBy),
                ('26000000-0000-0000-0000-000000000010', 'WIPER-BLADE-24',   'Wiper Blade 24 inch',       'مساحة زجاج 24 إنش',      '22000000-0000-0000-0000-000000000008',  8500, 0.62,  7600, 0.56,  5000, 0.36, FALSE, FALSE, 0, TRUE, '{}'::jsonb, '{}'::text[], now(), @CreatedBy)
            ON CONFLICT (code) DO NOTHING;
            """,
            new { CreatedBy = adminId },
            tx);

        await connection.ExecuteAsync(
            """
            INSERT INTO inventory_stock (id, sku_id, location_id, quantity_on_hand, quantity_reserved, updated_at)
            VALUES
                (uuid_generate_v4(), '26000000-0000-0000-0000-000000000001', '23000000-0000-0000-0000-000000000001', 45, 0, now()),
                (uuid_generate_v4(), '26000000-0000-0000-0000-000000000001', '23000000-0000-0000-0000-000000000002', 12, 0, now()),
                (uuid_generate_v4(), '26000000-0000-0000-0000-000000000001', '23000000-0000-0000-0000-000000000003',  8, 0, now()),
                (uuid_generate_v4(), '26000000-0000-0000-0000-000000000002', '23000000-0000-0000-0000-000000000001', 30, 0, now()),
                (uuid_generate_v4(), '26000000-0000-0000-0000-000000000002', '23000000-0000-0000-0000-000000000003',  5, 0, now()),
                (uuid_generate_v4(), '26000000-0000-0000-0000-000000000003', '23000000-0000-0000-0000-000000000001', 20, 0, now()),
                (uuid_generate_v4(), '26000000-0000-0000-0000-000000000003', '23000000-0000-0000-0000-000000000002',  8, 0, now()),
                (uuid_generate_v4(), '26000000-0000-0000-0000-000000000004', '23000000-0000-0000-0000-000000000001', 15, 0, now()),
                (uuid_generate_v4(), '26000000-0000-0000-0000-000000000004', '23000000-0000-0000-0000-000000000002',  6, 0, now()),
                (uuid_generate_v4(), '26000000-0000-0000-0000-000000000005', '23000000-0000-0000-0000-000000000001',120, 0, now()),
                (uuid_generate_v4(), '26000000-0000-0000-0000-000000000005', '23000000-0000-0000-0000-000000000002', 40, 0, now()),
                (uuid_generate_v4(), '26000000-0000-0000-0000-000000000005', '23000000-0000-0000-0000-000000000003', 20, 0, now()),
                (uuid_generate_v4(), '26000000-0000-0000-0000-000000000006', '23000000-0000-0000-0000-000000000001',  8, 0, now()),
                (uuid_generate_v4(), '26000000-0000-0000-0000-000000000006', '23000000-0000-0000-0000-000000000002',  2, 0, now()),
                (uuid_generate_v4(), '26000000-0000-0000-0000-000000000007', '23000000-0000-0000-0000-000000000001',  6, 0, now()),
                (uuid_generate_v4(), '26000000-0000-0000-0000-000000000009', '23000000-0000-0000-0000-000000000001', 10, 0, now()),
                (uuid_generate_v4(), '26000000-0000-0000-0000-000000000009', '23000000-0000-0000-0000-000000000002',  3, 0, now()),
                (uuid_generate_v4(), '26000000-0000-0000-0000-000000000010', '23000000-0000-0000-0000-000000000001', 35, 0, now()),
                (uuid_generate_v4(), '26000000-0000-0000-0000-000000000010', '23000000-0000-0000-0000-000000000002', 15, 0, now()),
                (uuid_generate_v4(), '26000000-0000-0000-0000-000000000010', '23000000-0000-0000-0000-000000000003', 10, 0, now())
            ON CONFLICT (sku_id, location_id) DO NOTHING;
            """,
            transaction: tx);

        await connection.ExecuteAsync(
            """
            INSERT INTO invoices (
                id, invoice_type, status, customer_id, invoice_date, due_date,
                subtotal_syp, subtotal_usd, discount_amount_syp, discount_amount_usd,
                delivery_fee_syp, delivery_fee_usd, tax_amount_syp, tax_amount_usd,
                total_syp, total_usd, paid_syp, paid_usd, fx_rate_id, fx_rate_snapshot,
                sales_rep_id, posted_at, posted_by, created_at, created_by)
            VALUES
                ('27000000-0000-0000-0000-000000000001', 'SALE', 'POSTED', '25000000-0000-0000-0000-000000000001', @Inv1Date, @Inv1Due, 260000, 19.26, 0, 0, 0, 0, 0, 0, 260000, 19.26, 260000, 19.26, '21000000-0000-0000-0000-000000000001', 13500, @CreatedBy, now(), @CreatedBy, now(), @CreatedBy),
                ('27000000-0000-0000-0000-000000000002', 'SALE', 'POSTED', '25000000-0000-0000-0000-000000000002', @Inv2Date, @Inv2Due, 436000, 32.30, 0, 0, 0, 0, 0, 0, 436000, 32.30,      0,  0.00, '21000000-0000-0000-0000-000000000001', 13500, @CreatedBy, now(), @CreatedBy, now(), @CreatedBy),
                ('27000000-0000-0000-0000-000000000003', 'SALE', 'POSTED', '25000000-0000-0000-0000-000000000003', @Inv3Date, @Inv3Due, 375000, 27.78, 0, 0, 0, 0, 0, 0, 375000, 27.78,      0,  0.00, '21000000-0000-0000-0000-000000000001', 13500, @CreatedBy, now(), @CreatedBy, now(), @CreatedBy),
                ('27000000-0000-0000-0000-000000000004', 'SALE', 'DRAFT',  '25000000-0000-0000-0000-000000000005', @Inv4Date, @Inv4Due, 108000,  8.00, 0, 0, 0, 0, 0, 0, 108000,  8.00,      0,  0.00, '21000000-0000-0000-0000-000000000001', 13500, @CreatedBy, NULL,  NULL,     now(), @CreatedBy),
                ('27000000-0000-0000-0000-000000000005', 'SALE', 'POSTED', '25000000-0000-0000-0000-000000000001', @Inv5Date, @Inv5Due, 325000, 24.07, 0, 0, 0, 0, 0, 0, 325000, 24.07, 100000,  7.41, '21000000-0000-0000-0000-000000000001', 13500, @CreatedBy, now(), @CreatedBy, now(), @CreatedBy)
            ON CONFLICT (id) DO NOTHING;
            """,
            new
            {
                CreatedBy = adminId,
                Inv1Date = today.AddDays(-5),
                Inv1Due = today.AddDays(25),
                Inv2Date = today.AddDays(-10),
                Inv2Due = today.AddDays(20),
                Inv3Date = today.AddDays(-45),
                Inv3Due = today.AddDays(-15),
                Inv4Date = today,
                Inv4Due = today.AddDays(30),
                Inv5Date = today.AddDays(-20),
                Inv5Due = today.AddDays(10)
            },
            tx);

        await connection.ExecuteAsync(
            """
            INSERT INTO invoice_lines (
                id, invoice_id, line_number, sku_id, location_id, quantity,
                unit_price_syp, unit_price_usd, discount_pct, cost_price_syp, cost_price_usd, fx_rate_used, is_price_override, created_at)
            VALUES
                ('27010000-0000-0000-0000-000000000001', '27000000-0000-0000-0000-000000000001', 1, '26000000-0000-0000-0000-000000000001', '23000000-0000-0000-0000-000000000001', 10, 15000, 1.10, 0,  9000, 0.65, 13500, FALSE, now()),
                ('27010000-0000-0000-0000-000000000002', '27000000-0000-0000-0000-000000000001', 2, '26000000-0000-0000-0000-000000000003', '23000000-0000-0000-0000-000000000001',  5, 22000, 1.60, 0, 13000, 0.95, 13500, FALSE, now()),
                ('27010000-0000-0000-0000-000000000003', '27000000-0000-0000-0000-000000000002', 1, '26000000-0000-0000-0000-000000000004', '23000000-0000-0000-0000-000000000001',  4, 85000, 6.20, 0, 52000, 3.80, 13500, FALSE, now()),
                ('27010000-0000-0000-0000-000000000004', '27000000-0000-0000-0000-000000000002', 2, '26000000-0000-0000-0000-000000000005', '23000000-0000-0000-0000-000000000001',  8, 12000, 0.88, 0,  7000, 0.51, 13500, FALSE, now()),
                ('27010000-0000-0000-0000-000000000005', '27000000-0000-0000-0000-000000000003', 1, '26000000-0000-0000-0000-000000000006', '23000000-0000-0000-0000-000000000001',  2, 95000, 6.90, 0, 58000, 4.20, 13500, FALSE, now()),
                ('27010000-0000-0000-0000-000000000006', '27000000-0000-0000-0000-000000000003', 2, '26000000-0000-0000-0000-000000000007', '23000000-0000-0000-0000-000000000001',  1,185000,13.50, 0,115000, 8.40, 13500, FALSE, now()),
                ('27010000-0000-0000-0000-000000000007', '27000000-0000-0000-0000-000000000004', 1, '26000000-0000-0000-0000-000000000002', '23000000-0000-0000-0000-000000000001',  6, 18000, 1.30, 0, 11000, 0.80, 13500, FALSE, now()),
                ('27010000-0000-0000-0000-000000000008', '27000000-0000-0000-0000-000000000005', 1, '26000000-0000-0000-0000-000000000005', '23000000-0000-0000-0000-000000000001', 20, 12000, 0.88, 0,  7000, 0.51, 13500, FALSE, now()),
                ('27010000-0000-0000-0000-000000000009', '27000000-0000-0000-0000-000000000005', 2, '26000000-0000-0000-0000-000000000010', '23000000-0000-0000-0000-000000000001', 10,  8500, 0.62, 0,  5000, 0.36, 13500, FALSE, now())
            ON CONFLICT (invoice_id, line_number) DO NOTHING;
            """,
            transaction: tx);

        await connection.ExecuteAsync(
            """
            INSERT INTO payments (
                id, payment_type, customer_id, payment_date, payment_method,
                amount_syp, amount_usd, allocated_syp, allocated_usd, fx_rate_id, received_by, created_at, created_by)
            VALUES
                ('28000000-0000-0000-0000-000000000001', 'RECEIPT', '25000000-0000-0000-0000-000000000001', @PaymentDate1, 'CASH', 260000, 19.26, 260000, 19.26, '21000000-0000-0000-0000-000000000001', @CreatedBy, now(), @CreatedBy),
                ('28000000-0000-0000-0000-000000000002', 'RECEIPT', '25000000-0000-0000-0000-000000000001', @PaymentDate2, 'CASH', 100000,  7.41, 100000,  7.41, '21000000-0000-0000-0000-000000000001', @CreatedBy, now(), @CreatedBy)
            ON CONFLICT (id) DO NOTHING;
            """,
            new { CreatedBy = adminId, PaymentDate1 = today.AddDays(-4), PaymentDate2 = today.AddDays(-18) },
            tx);

        await connection.ExecuteAsync(
            """
            INSERT INTO payment_allocations (id, payment_id, invoice_id, allocated_syp, allocated_usd, allocation_date, created_at, created_by)
            VALUES
                ('28010000-0000-0000-0000-000000000001', '28000000-0000-0000-0000-000000000001', '27000000-0000-0000-0000-000000000001', 260000, 19.26, @AllocDate1, now(), @CreatedBy),
                ('28010000-0000-0000-0000-000000000002', '28000000-0000-0000-0000-000000000002', '27000000-0000-0000-0000-000000000005', 100000,  7.41, @AllocDate2, now(), @CreatedBy)
            ON CONFLICT (payment_id, invoice_id) DO NOTHING;
            """,
            new { CreatedBy = adminId, AllocDate1 = today.AddDays(-4), AllocDate2 = today.AddDays(-18) },
            tx);

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
            """,
            transaction: tx);

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
            new { CreatedBy = adminId },
            tx);

        await tx.CommitAsync();
    }
}
