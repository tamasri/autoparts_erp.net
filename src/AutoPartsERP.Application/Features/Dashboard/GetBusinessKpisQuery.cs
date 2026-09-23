using AutoPartsERP.Application.Features.Accounting;
using AutoPartsERP.Application.Features.Wms;
using AutoPartsERP.Contracts.Dashboard;

namespace AutoPartsERP.Application.Features.Dashboard;

/// <summary>
/// Business KPIs for a period with optional filters. Every figure is aggregated in the database; a filter that has no meaning for a section
/// (a customer for purchases, a category for receivables) leaves that section unfiltered or empty, and the screen says so.
/// Net profit is not recomputed here: it is ERPNext's profit and loss for the period (the same query as the P&amp;L report).
/// </summary>
public sealed record GetBusinessKpisQuery(DateOnly From, DateOnly To, Guid? WarehouseId, Guid? CustomerId, Guid? SalesRepId, Guid? CategoryId)
    : IRequest<Result<BusinessKpisDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Invoices.Read;
}

public sealed class GetBusinessKpisQueryValidator : AbstractValidator<GetBusinessKpisQuery>
{
    public GetBusinessKpisQueryValidator()
    {
        RuleFor(x => x.To).GreaterThanOrEqualTo(x => x.From);
        RuleFor(x => x).Must(x => x.To.DayNumber - x.From.DayNumber <= 3660).WithMessage("The period can be at most ten years.");
    }
}

public sealed class GetBusinessKpisQueryHandler : IRequestHandler<GetBusinessKpisQuery, Result<BusinessKpisDto>>
{
    private const int SlowMoverDays = 90;

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;
    private readonly IErpNextClient _erpNext;
    private readonly ISender _sender;

    public GetBusinessKpisQueryHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser, IErpNextClient erpNext, ISender sender)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
        _erpNext = erpNext;
        _sender = sender;
    }

    // The locations of the chosen warehouse (itself and everything under it); empty when no warehouse is chosen.
    private const string WarehouseTree = """
        wh AS (
            SELECT id FROM locations WHERE id = @WarehouseId::uuid
            UNION
            SELECT l.id FROM locations l INNER JOIN wh ON l.parent_id = wh.id
        )
        """;

    private const string CategoryFilter = "(@CategoryId::uuid IS NULL OR c.path <@ (SELECT path FROM categories WHERE id = @CategoryId))";

    /// <summary>
    /// Posted sale and return lines, each scaled to its invoice's own net total (total without delivery and tax): the line keeps its share of
    /// the invoice discount, and the figures always add up to the invoices as the customer received them.
    /// </summary>
    private static string SaleLines(string from, string to) => $"""
        {WarehouseTree},
        lines AS (
            SELECT i.id AS invoice_id, i.invoice_date AS day, i.customer_id, i.sales_rep_id, il.sku_id,
                   CASE WHEN i.invoice_type = 'RETURN' THEN -1 ELSE 1 END AS sign,
                   abs(il.quantity) AS qty,
                   round(abs(il.line_total_usd) * CASE WHEN x.lines_usd = 0 THEN 0 ELSE (abs(i.total_usd) - i.delivery_fee_usd - i.tax_amount_usd) / x.lines_usd END, 4) AS net,
                   round(abs(il.quantity) * il.cost_price_usd, 4) AS cost
            FROM invoices i
            INNER JOIN invoice_lines il ON il.invoice_id = i.id
            CROSS JOIN LATERAL (SELECT sum(abs(y.line_total_usd)) AS lines_usd FROM invoice_lines y WHERE y.invoice_id = i.id) x
            INNER JOIN skus s ON s.id = il.sku_id
            LEFT JOIN categories c ON c.id = s.category_id
            WHERE i.status = 'POSTED' AND i.invoice_type IN ('SALE', 'RETURN') AND i.invoice_date BETWEEN {from} AND {to}
              AND (@CustomerId::uuid IS NULL OR i.customer_id = @CustomerId)
              AND (@SalesRepId::uuid IS NULL OR i.sales_rep_id = @SalesRepId)
              AND (@WarehouseId::uuid IS NULL OR il.location_id IN (SELECT id FROM wh))
              AND {CategoryFilter}
        )
        """;

    /// <summary>Posted supplier bill lines after the line discount and the bill discount.</summary>
    private static string PurchaseLines(string from, string to) => $"""
        {WarehouseTree},
        plines AS (
            SELECT p.id AS bill_id, p.bill_date AS day,
                   round(pl.line_total_usd * CASE WHEN p.subtotal_usd = 0 THEN 1 ELSE p.total_usd / p.subtotal_usd END, 4) AS amount
            FROM purchase_invoices p
            INNER JOIN purchase_invoice_lines pl ON pl.purchase_invoice_id = p.id
            INNER JOIN items it ON it.id = pl.item_id
            LEFT JOIN skus s ON s.id = it.sku_id
            LEFT JOIN categories c ON c.id = s.category_id
            WHERE p.status = 'POSTED' AND p.bill_date BETWEEN {from} AND {to}
              AND (@WarehouseId::uuid IS NULL OR p.warehouse_id IN (SELECT id FROM wh))
              AND {CategoryFilter}
        )
        """;

    private const string ReceiptUsd = "CASE WHEN p.amount_usd > 0 THEN p.amount_usd WHEN f.mid_rate > 0 THEN round(p.amount_syp / f.mid_rate, 4) ELSE 0 END";

    public async Task<Result<BusinessKpisDto>> Handle(GetBusinessKpisQuery q, CancellationToken cancellationToken)
    {
        var trendFrom = new DateOnly(q.To.Year, q.To.Month, 1).AddMonths(-11);
        var args = new
        {
            q.From, q.To, q.WarehouseId, q.CustomerId, q.SalesRepId, q.CategoryId, TrendFrom = trendFrom,
            SlowSince = q.To.AddDays(-SlowMoverDays),
            ScopeAll = WarehouseScopeSql.SeesAll(_currentUser), ScopeUser = _currentUser.UserId
        };
        var byParty = q.CustomerId is not null || q.SalesRepId is not null;
        var byGoods = q.WarehouseId is not null || q.CategoryId is not null;
        var seesPurchases = _currentUser.HasPermission(PermissionCodes.Purchases.Read) && !byParty;
        var seesCash = _currentUser.HasPermission(PermissionCodes.Payments.Read) && q.SalesRepId is null && !byGoods;
        var seesStock = _currentUser.HasPermission(PermissionCodes.Inventory.Read) && !byParty;

        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        Task<IEnumerable<T>> Query<T>(string sql) => connection.QueryAsync<T>(new CommandDefinition(sql, args, cancellationToken: cancellationToken));

        // ---- sales
        var s = (await Query<(decimal Gross, decimal Returns, decimal Cost, int Invoices, int Customers)>($"""
            WITH RECURSIVE {SaleLines("@From", "@To")}
            SELECT COALESCE(sum(net) FILTER (WHERE sign = 1), 0), COALESCE(sum(net) FILTER (WHERE sign = -1), 0), COALESCE(sum(sign * cost), 0),
                   (count(DISTINCT invoice_id) FILTER (WHERE sign = 1))::int, (count(DISTINCT customer_id) FILTER (WHERE sign = 1))::int
            FROM lines;
            """)).Single();
        var net = s.Gross - s.Returns;
        var gp = net - s.Cost;
        var sales = new KpiSalesDto(s.Gross, s.Returns, net, s.Cost, gp, KpiMath.MarginPct(net, gp), s.Invoices, KpiMath.Average(s.Gross, s.Invoices), s.Customers);

        var topCustomers = (await Query<KpiRankDto>($"""
            WITH RECURSIVE {SaleLines("@From", "@To")}
            SELECT l.customer_id AS Id, c.name AS Name, sum(l.sign * l.net) AS AmountUsd, sum(l.sign * (l.net - l.cost)) AS Secondary, (count(DISTINCT l.invoice_id))::int AS Count
            FROM lines l INNER JOIN customers c ON c.id = l.customer_id
            GROUP BY l.customer_id, c.name ORDER BY 3 DESC LIMIT 10;
            """)).ToList();
        var topItems = (await Query<KpiRankDto>($"""
            WITH RECURSIVE {SaleLines("@From", "@To")}
            SELECT l.sku_id AS Id, k.code || ' — ' || COALESCE(NULLIF(k.name_ar, ''), k.name) AS Name, sum(l.sign * l.net) AS AmountUsd,
                   sum(l.sign * l.qty) AS Secondary, (count(DISTINCT l.invoice_id))::int AS Count
            FROM lines l INNER JOIN skus k ON k.id = l.sku_id
            GROUP BY l.sku_id, k.code, k.name, k.name_ar ORDER BY 3 DESC LIMIT 10;
            """)).ToList();
        var byRep = (await Query<KpiRankDto>($"""
            WITH RECURSIVE {SaleLines("@From", "@To")}
            SELECT l.sales_rep_id AS Id, COALESCE(NULLIF(u.full_name, ''), u.user_name, 'بدون مندوب') AS Name, sum(l.sign * l.net) AS AmountUsd,
                   sum(l.sign * (l.net - l.cost)) AS Secondary, (count(DISTINCT l.invoice_id))::int AS Count
            FROM lines l LEFT JOIN asp_net_users u ON u.id = l.sales_rep_id
            GROUP BY l.sales_rep_id, u.full_name, u.user_name ORDER BY 3 DESC;
            """)).ToList();

        // ---- monthly trend: the twelve months ending with the period's last month
        var monthSales = (await Query<(DateTime Month, decimal Net, decimal Profit)>($"""
            WITH RECURSIVE {SaleLines("@TrendFrom", "@To")}
            SELECT date_trunc('month', day) AS Month, sum(sign * net), sum(sign * (net - cost)) FROM lines GROUP BY 1;
            """)).ToDictionary(x => DateOnly.FromDateTime(x.Month), x => (x.Net, x.Profit));
        var monthPurchases = seesPurchases
            ? (await Query<(DateTime Month, decimal Amount)>($"WITH RECURSIVE {PurchaseLines("@TrendFrom", "@To")} SELECT date_trunc('month', day), sum(amount) FROM plines GROUP BY 1;"))
                .ToDictionary(x => DateOnly.FromDateTime(x.Month), x => x.Amount)
            : [];
        var monthReceipts = seesCash
            ? (await Query<(DateTime Month, decimal Amount)>($"""
                SELECT date_trunc('month', p.payment_date), sum({ReceiptUsd})
                FROM payments p LEFT JOIN fx_rates f ON f.id = p.fx_rate_id
                WHERE p.payment_type = 'RECEIPT' AND NOT p.is_reversed AND p.payment_date BETWEEN @TrendFrom AND @To
                  AND (@CustomerId::uuid IS NULL OR p.customer_id = @CustomerId)
                GROUP BY 1;
                """)).ToDictionary(x => DateOnly.FromDateTime(x.Month), x => x.Amount)
            : [];
        var months = Enumerable.Range(0, 12).Select(i => trendFrom.AddMonths(i)).Select(m => new KpiMonthDto(
            m.Year, m.Month,
            monthSales.TryGetValue(m, out var ms) ? ms.Net : 0,
            monthSales.TryGetValue(m, out var mp) ? mp.Profit : 0,
            monthPurchases.GetValueOrDefault(m),
            monthReceipts.GetValueOrDefault(m))).ToList();

        // ---- receivables (by customer / rep; goods filters do not apply to a balance)
        const string openInvoices = """
            FROM invoices i INNER JOIN customers c ON c.id = i.customer_id
            WHERE i.status = 'POSTED' AND i.invoice_type = 'SALE' AND i.balance_usd > 0 AND i.invoice_date <= @To
              AND (@CustomerId::uuid IS NULL OR i.customer_id = @CustomerId)
              AND (@SalesRepId::uuid IS NULL OR i.sales_rep_id = @SalesRepId)
            """;
        var open = (await Query<(DateOnly DueDate, decimal Balance)>($"SELECT i.due_date, i.balance_usd {openInvoices};")).ToList();
        var receivables = KpiMath.Ageing(open, q.To, KpiMath.Dso(open.Sum(o => o.Balance), net, q.From, q.To));
        var overdue = (await Query<KpiOverdueDto>($"""
            SELECT i.id AS InvoiceId, i.invoice_number AS InvoiceNumber, c.name AS CustomerName, i.due_date AS DueDate,
                   (@To::date - i.due_date)::int AS DaysOverdue, i.balance_usd AS BalanceUsd
            {openInvoices} AND i.due_date < @To
            ORDER BY i.balance_usd DESC LIMIT 10;
            """)).ToList();

        // ---- purchases, payables, cash
        KpiPurchasesDto? purchases = null;
        KpiAgeingDto? payables = null;
        if (seesPurchases)
        {
            var p = (await Query<(decimal Total, int Bills)>($"WITH RECURSIVE {PurchaseLines("@From", "@To")} SELECT COALESCE(sum(amount), 0), (count(DISTINCT bill_id))::int FROM plines;")).Single();
            purchases = new KpiPurchasesDto(p.Total, p.Bills);
            var bills = (await Query<(DateOnly DueDate, decimal Balance)>($"""
                WITH RECURSIVE {WarehouseTree}
                SELECT p.due_date, p.balance_usd FROM purchase_invoices p
                WHERE p.status = 'POSTED' AND p.balance_usd > 0 AND p.bill_date <= @To
                  AND (@WarehouseId::uuid IS NULL OR p.warehouse_id IN (SELECT id FROM wh));
                """)).ToList();
            payables = KpiMath.Ageing(bills, q.To, null);
        }

        KpiCashDto? cash = null;
        if (seesCash)
        {
            var receipts = (await Query<decimal>($"""
                SELECT COALESCE(sum({ReceiptUsd}), 0)
                FROM payments p LEFT JOIN fx_rates f ON f.id = p.fx_rate_id
                WHERE p.payment_type = 'RECEIPT' AND NOT p.is_reversed AND p.payment_date BETWEEN @From AND @To
                  AND (@CustomerId::uuid IS NULL OR p.customer_id = @CustomerId);
                """)).Single();
            decimal? paid = seesPurchases
                ? (await Query<decimal>("SELECT COALESCE(sum(amount_usd), 0) FROM supplier_payments WHERE NOT is_reversed AND payment_date BETWEEN @From AND @To;")).Single()
                : null;
            cash = new KpiCashDto(receipts, paid, paid is null ? null : receipts - paid);
        }

        // ---- stock (today) and slow movers
        KpiStockDto? stock = null;
        var slowMovers = new List<KpiSlowMoverDto>();
        if (seesStock)
        {
            var stockSql = $"""
                WITH RECURSIVE {WarehouseTree},
                st AS (
                    SELECT st.sku_id, st.quantity_on_hand AS qty, st.quantity_available AS avail
                    FROM inventory_stock st
                    WHERE (@WarehouseId::uuid IS NULL OR st.location_id IN (SELECT id FROM wh))
                      AND (@ScopeAll OR st.location_id IN (SELECT location_id FROM user_visible_locations(@ScopeUser)))
                ),
                per AS (
                    SELECT s.id AS sku_id, s.code, COALESCE(NULLIF(s.name_ar, ''), s.name) AS name, s.cost_price_usd AS cost, s.reorder_level,
                           COALESCE(sum(st.qty), 0) AS qty, COALESCE(sum(st.avail), 0) AS avail
                    FROM skus s LEFT JOIN st ON st.sku_id = s.id LEFT JOIN categories c ON c.id = s.category_id
                    WHERE s.is_active AND {CategoryFilter}
                    GROUP BY s.id
                ),
                sold AS (
                    SELECT il.sku_id, max(i.invoice_date) AS last
                    FROM invoice_lines il INNER JOIN invoices i ON i.id = il.invoice_id
                    WHERE i.status = 'POSTED' AND i.invoice_type = 'SALE' AND i.invoice_date <= @To
                    GROUP BY il.sku_id
                ),
                slow AS (
                    SELECT per.*, sold.last FROM per LEFT JOIN sold ON sold.sku_id = per.sku_id
                    WHERE per.qty > 0 AND (sold.last IS NULL OR sold.last < @SlowSince)
                )
                """;
            stock = (await Query<KpiStockDto>($"""
                {stockSql}
                SELECT COALESCE((SELECT sum(qty * cost) FROM per), 0) AS ValueUsd,
                       (SELECT count(*) FROM per WHERE avail > 0)::int AS SkusInStock,
                       (SELECT count(*) FROM per WHERE avail <= 0)::int AS SkusOutOfStock,
                       (SELECT count(*) FROM per WHERE avail > 0 AND avail <= reorder_level)::int AS SkusBelowReorder,
                       (SELECT count(*) FROM slow)::int AS SlowMoverCount,
                       COALESCE((SELECT sum(qty * cost) FROM slow), 0) AS SlowMoverValueUsd;
                """)).Single();
            slowMovers = (await Query<KpiSlowMoverDto>($"""
                {stockSql}
                SELECT sku_id AS SkuId, code AS Code, name AS Name, qty AS Quantity, qty * cost AS ValueUsd, last AS LastSale
                FROM slow ORDER BY qty * cost DESC LIMIT 10;
                """)).ToList();
        }

        // ---- net profit from the ledger, only for the whole company
        KpiLedgerDto? ledger = null;
        string? ledgerNote = null;
        if (byParty || byGoods)
        {
            ledgerNote = "صافي الربح من دفتر الأستاذ متاح للمنشأة كاملة فقط (بدون فلاتر).";
        }
        else if (!_currentUser.HasPermission(PermissionCodes.Accounting.Read))
        {
            ledgerNote = "صافي الربح يتطلب صلاحية قراءة المحاسبة.";
        }
        else if (!_erpNext.IsEnabled)
        {
            ledgerNote = "ERPNext غير مفعّل؛ صافي الربح غير متاح.";
        }
        else
        {
            var pl = await _sender.Send(new GetProfitLossStatementQuery(q.From, q.To), cancellationToken);
            if (pl.IsSuccess)
            {
                ledger = new KpiLedgerDto(pl.Value!.TotalIncome, pl.Value.TotalExpenses, pl.Value.NetProfit);
            }
            else
            {
                ledgerNote = $"تعذرت قراءة الأرباح والخسائر من ERPNext: {pl.Error.Message}";
            }
        }

        return Result<BusinessKpisDto>.Success(new BusinessKpisDto(
            q.From, q.To, sales, ledger, ledgerNote, purchases, receivables, payables, cash, stock, months, topCustomers, topItems, byRep, slowMovers, overdue));
    }
}
