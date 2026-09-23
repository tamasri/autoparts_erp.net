using System.Globalization;
using AutoPartsERP.Application.Features.Customers.GetCustomerAccountStatement;
using AutoPartsERP.Application.Features.Dashboard;
using Dapper;

namespace AutoPartsERP.Application.Features.Assistant;

public sealed record AssistantReply(string Text, PendingChoice? Pending = null);

/// <summary>
/// Answers one question as the signed-in user (the WhatsApp number's ERP user): every answer checks that user's permissions and
/// warehouse scope, and reuses the application's own queries where one exists (account statement, business KPIs), so the numbers are
/// the same as on the screens. The text is written here, on the server — the language model never sees it.
/// </summary>
public sealed class AssistantAnswers
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;
    private readonly ISender _sender;
    private readonly TimeSpan _utcOffset;

    public AssistantAnswers(IDbConnectionFactory connectionFactory, ICurrentUser currentUser, ISender sender, AssistantOptions options)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
        _sender = sender;
        _utcOffset = TimeSpan.FromHours(options.UtcOffsetHours);
    }

    private DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow + _utcOffset);

    public const string NoPermission = "ليس لديك صلاحية لهذا السؤال في النظام.";

    public static string HelpText =>
        """
        أهلاً 👋 يمكنني الإجابة عن:
        • رصيد زبون — مثال: رصيد محمد الخطيب
        • مخزون صنف — مثال: كم يوجد فلتر زيت تويوتا
        • حالة فاتورة — مثال: فاتورة 41
        • المبيعات — مثال: مبيعات اليوم / مبيعات الشهر
        • الفواتير المتأخرة — مثال: الفواتير المتأخرة لشركة الأفق
        للاستعلام فقط؛ لا أنشئ ولا أعدّل أي مستند.
        """;

    public async Task<AssistantReply> AnswerAsync(AssistantIntent intent, Guid? chosenId, CancellationToken cancellationToken)
    {
        return intent.Action switch
        {
            AssistantActions.CustomerBalance => await CustomerBalanceAsync(intent, chosenId, cancellationToken),
            AssistantActions.ItemStock => await ItemStockAsync(intent, chosenId, cancellationToken),
            AssistantActions.InvoiceStatus => await InvoiceStatusAsync(intent, chosenId, cancellationToken),
            AssistantActions.SalesSummary => await SalesSummaryAsync(intent, cancellationToken),
            AssistantActions.OverdueInvoices => await OverdueAsync(intent, chosenId, cancellationToken),
            _ => new AssistantReply(HelpText),
        };
    }

    private async Task<AssistantReply> CustomerBalanceAsync(AssistantIntent intent, Guid? chosenId, CancellationToken ct)
    {
        if (!_currentUser.HasPermission(PermissionCodes.Reports.AccountStatement))
        {
            return new AssistantReply(NoPermission);
        }

        var customerId = chosenId;
        if (customerId is null)
        {
            var name = intent.Arg("customer_name");
            if (name is null)
            {
                return new AssistantReply("اكتب اسم الزبون، مثلاً: رصيد محمد الخطيب");
            }

            var resolved = await ResolveAsync(intent, "customer_name", "زبون", EntityMatcher.FindCustomerAsync, ct);
            if (resolved.Reply is not null)
            {
                return resolved.Reply;
            }

            customerId = resolved.Id;
        }

        var statement = await _sender.Send(new GetCustomerAccountStatementQuery(customerId!.Value), ct);
        if (statement.IsFailure)
        {
            return new AssistantReply(statement.Error.Code.StartsWith("Authorization.", StringComparison.Ordinal) ? NoPermission : "تعذّر جلب كشف الحساب.");
        }

        var s = statement.Value!;
        return new AssistantReply(
            $"""
            رصيد الزبون {s.CustomerName} ({s.CustomerCode}):
            المتبقي عليه: {Money(s.OutstandingSyp, s.OutstandingUsd)}
            مجموع الفواتير: {Money(s.TotalInvoicedSyp, s.TotalInvoicedUsd)}
            المدفوع: {Money(s.TotalPaidSyp, s.TotalPaidUsd)}
            """);
    }

    private async Task<AssistantReply> ItemStockAsync(AssistantIntent intent, Guid? chosenId, CancellationToken ct)
    {
        if (!_currentUser.HasPermission(PermissionCodes.Inventory.Read))
        {
            return new AssistantReply(NoPermission);
        }

        var skuId = chosenId;
        if (skuId is null)
        {
            if (intent.Arg("item") is null)
            {
                return new AssistantReply("اكتب اسم الصنف أو رقمه، مثلاً: كم يوجد فلتر زيت تويوتا");
            }

            var resolved = await ResolveAsync(intent, "item", "صنف", EntityMatcher.FindItemAsync, ct);
            if (resolved.Reply is not null)
            {
                return resolved.Reply;
            }

            skuId = resolved.Id;
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct);
        var sku = await connection.QuerySingleAsync<(string Label, string Code)>(new CommandDefinition(
            "SELECT COALESCE(NULLIF(name_ar, ''), name) AS Label, code AS Code FROM skus WHERE id = @skuId;", new { skuId }, cancellationToken: ct));
        var allWarehouses = _currentUser.HasPermission(PermissionCodes.Inventory.AllWarehouses);
        var rows = (await connection.QueryAsync<(string Location, decimal Available)>(new CommandDefinition(
            """
            SELECT l.name AS Location, st.quantity_available AS Available
            FROM inventory_stock st
            INNER JOIN locations l ON l.id = st.location_id
            WHERE st.sku_id = @skuId AND st.quantity_available > 0
              AND (@allWarehouses OR st.location_id IN (SELECT location_id FROM user_visible_locations(@userId)))
            ORDER BY st.quantity_available DESC, l.name;
            """,
            new { skuId, allWarehouses, userId = _currentUser.UserId }, cancellationToken: ct))).ToList();

        var warehouse = intent.Arg("warehouse");
        var note = string.Empty;
        if (warehouse is not null)
        {
            var wanted = rows.Where(r => r.Location.Contains(warehouse, StringComparison.OrdinalIgnoreCase)).ToList();
            if (wanted.Count > 0)
            {
                rows = wanted;
            }
            else
            {
                note = $"\n(لا يوجد منه في «{warehouse}»؛ هذه بقية المستودعات)";
            }
        }

        if (rows.Count == 0)
        {
            return new AssistantReply($"لا يوجد مخزون متاح من {sku.Label} ({sku.Code}){(allWarehouses ? string.Empty : " في مستودعاتك")}.");
        }

        var lines = string.Join("\n", rows.Select(r => $"• {r.Location}: {Qty(r.Available)}"));
        return new AssistantReply($"المتاح من {sku.Label} ({sku.Code}):\n{lines}\nالمجموع: {Qty(rows.Sum(r => r.Available))}{note}");
    }

    private sealed record InvoiceRow(
        Guid Id, string InvoiceNumber, string Status, string Type, DateOnly InvoiceDate, DateOnly DueDate, string Customer,
        decimal TotalSyp, decimal TotalUsd, decimal PaidSyp, decimal PaidUsd, decimal BalanceSyp, decimal BalanceUsd);

    private const string InvoiceSelect =
        """
        SELECT i.id AS Id, i.invoice_number AS InvoiceNumber, i.status AS Status, i.invoice_type AS Type, i.invoice_date AS InvoiceDate,
               i.due_date AS DueDate, c.name AS Customer, i.total_syp AS TotalSyp, i.total_usd AS TotalUsd, i.paid_syp AS PaidSyp,
               i.paid_usd AS PaidUsd, i.balance_syp AS BalanceSyp, i.balance_usd AS BalanceUsd
        FROM invoices i INNER JOIN customers c ON c.id = i.customer_id
        """;

    private async Task<AssistantReply> InvoiceStatusAsync(AssistantIntent intent, Guid? chosenId, CancellationToken ct)
    {
        if (!_currentUser.HasPermission(PermissionCodes.Invoices.Read))
        {
            return new AssistantReply(NoPermission);
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct);
        List<InvoiceRow> found;
        if (chosenId is { } id)
        {
            found = (await connection.QueryAsync<InvoiceRow>(new CommandDefinition(InvoiceSelect + " WHERE i.id = @id;", new { id }, cancellationToken: ct))).ToList();
        }
        else
        {
            var raw = LatinDigits(intent.Arg("invoice_number") ?? string.Empty).Trim().ToUpperInvariant();
            var digits = new string(raw.Where(char.IsAsciiDigit).ToArray()).TrimStart('0');
            if (digits.Length == 0)
            {
                return new AssistantReply("اكتب رقم الفاتورة، مثلاً: فاتورة 41");
            }

            // Full number first; otherwise the number's sequence part ("41" → INV-2026-00041), newest first.
            found = (await connection.QueryAsync<InvoiceRow>(new CommandDefinition(
                InvoiceSelect + " WHERE upper(i.invoice_number) = @raw OR i.invoice_number ~ ('-0*' || @digits || '$') ORDER BY (upper(i.invoice_number) = @raw) DESC, i.invoice_date DESC LIMIT 5;",
                new { raw, digits }, cancellationToken: ct))).ToList();
            if (found.Count > 1 && !string.Equals(found[0].InvoiceNumber, raw, StringComparison.OrdinalIgnoreCase))
            {
                var options = found.Select(f => new PendingOption(f.Id, $"{f.InvoiceNumber} — {f.Customer} ({f.InvoiceDate:yyyy-MM-dd})")).ToList();
                return Choose(intent, "فاتورة", raw, options);
            }
        }

        if (found.Count == 0)
        {
            return new AssistantReply($"لم أجد فاتورة بالرقم «{intent.Arg("invoice_number")}».");
        }

        var inv = found[0];
        // A void creates a CREDIT_NOTE that reverses the invoice; returns are RETURN.
        var kind = inv.Type switch { "RETURN" => "مرتجع", "CREDIT_NOTE" => "إشعار دائن (عكس فاتورة ملغاة)", _ => "فاتورة" };
        var overdue = inv.Status == "POSTED" && inv.BalanceUsd > 0.005m && inv.DueDate < Today ? $" — متأخرة {Today.DayNumber - inv.DueDate.DayNumber} يوماً" : string.Empty;
        return new AssistantReply(
            $"""
            {kind} {inv.InvoiceNumber} — {inv.Customer}
            الحالة: {StatusAr(inv.Status)}
            التاريخ: {inv.InvoiceDate:yyyy-MM-dd} · الاستحقاق: {inv.DueDate:yyyy-MM-dd}{overdue}
            الإجمالي: {Money(inv.TotalSyp, inv.TotalUsd)}
            المدفوع: {Money(inv.PaidSyp, inv.PaidUsd)}
            المتبقي: {Money(inv.BalanceSyp, inv.BalanceUsd)}
            """);
    }

    private async Task<AssistantReply> SalesSummaryAsync(AssistantIntent intent, CancellationToken ct)
    {
        if (!_currentUser.HasPermission(PermissionCodes.Invoices.Read))
        {
            return new AssistantReply(NoPermission);
        }

        var period = intent.Arg("period") ?? "today";
        var today = Today;
        var (from, to, label) = period switch
        {
            "yesterday" => (today.AddDays(-1), today.AddDays(-1), "أمس"),
            "this_week" => (today.AddDays(-(((int)today.DayOfWeek + 1) % 7)), today, "هذا الأسبوع"), // weeks start on Saturday
            "this_month" => (new DateOnly(today.Year, today.Month, 1), today, "هذا الشهر"),
            "last_month" => (new DateOnly(today.Year, today.Month, 1).AddMonths(-1), new DateOnly(today.Year, today.Month, 1).AddDays(-1), "الشهر الماضي"),
            _ => (today, today, "اليوم"),
        };

        var kpis = await _sender.Send(new GetBusinessKpisQuery(from, to, null, null, null, null), ct);
        if (kpis.IsFailure)
        {
            return new AssistantReply(kpis.Error.Code.StartsWith("Authorization.", StringComparison.Ordinal) ? NoPermission : "تعذّر حساب المبيعات.");
        }

        var s = kpis.Value!.Sales;
        var rate = await LatestRateAsync(ct);
        var margin = s.GrossMarginPct is { } m ? $" ({m.ToString("0.#", CultureInfo.InvariantCulture)}٪)" : string.Empty;
        return new AssistantReply(
            $"""
            مبيعات {label} ({from:yyyy-MM-dd}{(from == to ? string.Empty : $" → {to:yyyy-MM-dd}")}):
            صافي المبيعات: {UsdWithLira(s.NetSalesUsd, rate)}
            المرتجعات: {UsdWithLira(s.ReturnsUsd, rate)}
            مجمل الربح: {UsdWithLira(s.GrossProfitUsd, rate)}{margin}
            عدد الفواتير: {s.InvoiceCount} · الزبائن: {s.CustomerCount}
            """);
    }

    private async Task<AssistantReply> OverdueAsync(AssistantIntent intent, Guid? chosenId, CancellationToken ct)
    {
        if (!_currentUser.HasPermission(PermissionCodes.Invoices.Read))
        {
            return new AssistantReply(NoPermission);
        }

        var customerId = chosenId;
        if (customerId is null && intent.Arg("customer_name") is not null)
        {
            var resolved = await ResolveAsync(intent, "customer_name", "زبون", EntityMatcher.FindCustomerAsync, ct);
            if (resolved.Reply is not null)
            {
                return resolved.Reply;
            }

            customerId = resolved.Id;
        }

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct);
        var rows = (await connection.QueryAsync<InvoiceRow>(new CommandDefinition(
            InvoiceSelect + """
             WHERE i.status = 'POSTED' AND i.invoice_type = 'SALE' AND i.balance_usd > 0.005 AND i.due_date < @today
               AND (@customerId::uuid IS NULL OR i.customer_id = @customerId)
             ORDER BY i.due_date, i.invoice_number;
            """,
            new { today = Today, customerId }, cancellationToken: ct))).ToList();

        if (rows.Count == 0)
        {
            return new AssistantReply(customerId is null ? "لا توجد فواتير متأخرة 👍" : "لا توجد فواتير متأخرة على هذا الزبون 👍");
        }

        var shown = rows.Take(10).Select(r => $"• {r.InvoiceNumber} — {r.Customer}، متأخرة {Today.DayNumber - r.DueDate.DayNumber} يوماً، المتبقي {Money(r.BalanceSyp, r.BalanceUsd)}");
        var more = rows.Count > 10 ? $"\n… و{rows.Count - 10} فواتير أخرى" : string.Empty;
        return new AssistantReply(
            $"الفواتير المتأخرة ({rows.Count})، المجموع {Money(rows.Sum(r => r.BalanceSyp), rows.Sum(r => r.BalanceUsd))}:\n{string.Join("\n", shown)}{more}");
    }

    private delegate Task<MatchOutcome> Finder(IDbConnection connection, string text, CancellationToken ct);

    /// <summary>Matches the named record; returns a reply instead of an id when nothing or more than one plausible record matched.</summary>
    private async Task<(Guid? Id, AssistantReply? Reply)> ResolveAsync(AssistantIntent intent, string arg, string noun, Finder find, CancellationToken ct)
    {
        var text = intent.Arg(arg)!;
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct);
        var outcome = await find(connection, text, ct);
        return outcome.Kind switch
        {
            MatchKind.Found => (outcome.Match!.Id, null),
            MatchKind.NotFound => (null, new AssistantReply($"لم أجد {noun}اً باسم «{text}». جرّب كتابة الاسم بشكل آخر أو جزءاً منه.")),
            _ => (null, Choose(intent, noun, text, outcome.Candidates.Select(c => new PendingOption(c.Id, c.Detail is null ? c.Label : $"{c.Label} ({c.Detail})")).ToList())),
        };
    }

    private static AssistantReply Choose(AssistantIntent intent, string noun, string text, IReadOnlyList<PendingOption> options)
    {
        var list = string.Join("\n", options.Select((o, i) => $"{i + 1}. {o.Label}"));
        return new AssistantReply(
            $"لم أطابق «{text}» بدقة. هل تقصد:\n{list}\n(أرسل رقم الخيار، أو 0 للإلغاء)",
            new PendingChoice(intent.Action, new Dictionary<string, string>(intent.Args, StringComparer.Ordinal), options));
    }

    private async Task<decimal?> LatestRateAsync(CancellationToken ct)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(ct);
        return await connection.ExecuteScalarAsync<decimal?>(new CommandDefinition(
            "SELECT mid_rate FROM fx_rates WHERE is_active ORDER BY rate_date DESC, created_at DESC LIMIT 1;", cancellationToken: ct));
    }

    private static string StatusAr(string status) => status switch
    {
        "DRAFT" => "مسودة",
        "CONFIRMED" => "مؤكدة (غير مرحّلة)",
        "POSTED" => "مرحّلة",
        "VOID" => "ملغاة",
        _ => status,
    };

    /// <summary>Lira first, dollars in brackets (the owner's display rule); amounts as recorded on the documents.</summary>
    public static string Money(decimal syp, decimal usd) =>
        $"{syp.ToString("#,0", CultureInfo.InvariantCulture)} ل.س (${usd.ToString("#,0.00", CultureInfo.InvariantCulture)})";

    private static string UsdWithLira(decimal usd, decimal? rate) =>
        rate is { } r and > 0 ? Money(Math.Round(usd * r, 0), usd) : $"${usd.ToString("#,0.00", CultureInfo.InvariantCulture)}";

    private static string Qty(decimal q) => q.ToString("#,0.##", CultureInfo.InvariantCulture);

    public static string LatinDigits(string s) =>
        new(s.Select(ch => ch is >= '٠' and <= '٩' ? (char)('0' + (ch - '٠')) : ch is >= '۰' and <= '۹' ? (char)('0' + (ch - '۰')) : ch).ToArray());
}
