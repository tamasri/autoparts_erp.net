using AutoPartsERP.Contracts.Dashboard;

namespace AutoPartsERP.Application.Features.Dashboard;

/// <summary>
/// Every dashboard figure is aggregated in the database over the full data set. The previous
/// screens summed a browser-side sample page (5-200 rows), which is wrong as soon as the data grows.
/// Money figures come from POSTED sale invoices only; stock figures from the operational
/// <c>inventory_stock</c> / <c>skus</c> tables that drive invoicing.
/// </summary>
public sealed record GetDashboardSummaryQuery()
    : IRequest<Result<DashboardSummaryDto>>, IAuthorizedRequest
{
    // The summary is derived from invoices, so it requires the same permission as reading them.
    public string RequiredPermission => PermissionCodes.Invoices.Read;
}

public sealed class GetDashboardSummaryQueryHandler : IRequestHandler<GetDashboardSummaryQuery, Result<DashboardSummaryDto>>
{
    private const int SalesDays = 30;

    private readonly IDbConnectionFactory _connectionFactory;

    public GetDashboardSummaryQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<DashboardSummaryDto>> Handle(GetDashboardSummaryQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var money = await connection.QuerySingleAsync<MoneyRow>(new CommandDefinition(
            """
            SELECT
                (SELECT COUNT(*) FROM customers WHERE is_active = TRUE)::int                                   AS ActiveCustomers,
                COUNT(*) FILTER (WHERE status = 'POSTED')::int                                                 AS PostedInvoices,
                COALESCE(SUM(balance_syp) FILTER (WHERE status = 'POSTED'), 0)                                 AS ReceivablesSyp,
                COALESCE(SUM(balance_usd) FILTER (WHERE status = 'POSTED'), 0)                                 AS ReceivablesUsd,
                COUNT(*) FILTER (WHERE status = 'POSTED' AND balance_syp > 0 AND due_date < CURRENT_DATE)::int AS OverdueInvoices,
                COALESCE(SUM(balance_syp) FILTER (WHERE status = 'POSTED' AND balance_syp > 0 AND due_date < CURRENT_DATE), 0) AS OverdueSyp,
                COALESCE(SUM(total_syp) FILTER (WHERE status = 'POSTED' AND invoice_date = CURRENT_DATE), 0)   AS SalesTodaySyp,
                COALESCE(SUM(total_usd) FILTER (WHERE status = 'POSTED' AND invoice_date = CURRENT_DATE), 0)   AS SalesTodayUsd,
                COALESCE(SUM(total_syp) FILTER (WHERE status = 'POSTED' AND invoice_date >= date_trunc('month', CURRENT_DATE)::date), 0) AS SalesMonthSyp,
                COALESCE(SUM(total_usd) FILTER (WHERE status = 'POSTED' AND invoice_date >= date_trunc('month', CURRENT_DATE)::date), 0) AS SalesMonthUsd
            FROM invoices
            WHERE invoice_type = 'SALE';
            """,
            cancellationToken: cancellationToken));

        var stock = await connection.QuerySingleAsync<StockRow>(new CommandDefinition(
            """
            WITH per_sku AS (
                SELECT s.id, s.reorder_level, COALESCE(SUM(st.quantity_available), 0) AS available
                FROM skus s
                LEFT JOIN inventory_stock st ON st.sku_id = s.id
                WHERE s.is_active = TRUE
                GROUP BY s.id, s.reorder_level
            )
            SELECT
                COUNT(*) FILTER (WHERE available > 0)::int                                AS SkusInStock,
                COUNT(*) FILTER (WHERE available <= 0)::int                               AS SkusOutOfStock,
                COUNT(*) FILTER (WHERE available > 0 AND available <= reorder_level)::int AS SkusLowStock,
                (SELECT COUNT(*) FROM inventory_alerts WHERE status IN ('OPEN', 'ACKNOWLEDGED'))::int AS OpenAlerts
            FROM per_sku;
            """,
            cancellationToken: cancellationToken));

        var byDay = (await connection.QueryAsync<SalesDayRow>(new CommandDefinition(
            """
            SELECT d.day::date AS Day,
                   COALESCE(SUM(i.total_syp), 0) AS TotalSyp,
                   COALESCE(SUM(i.total_usd), 0) AS TotalUsd,
                   COUNT(i.id)::int AS InvoiceCount
            FROM generate_series(CURRENT_DATE - (@Days - 1), CURRENT_DATE, interval '1 day') AS d(day)
            LEFT JOIN invoices i
                   ON i.invoice_date = d.day::date AND i.status = 'POSTED' AND i.invoice_type = 'SALE'
            GROUP BY d.day
            ORDER BY d.day;
            """,
            new { Days = SalesDays },
            cancellationToken: cancellationToken))).ToList();

        var top = (await connection.QueryAsync<TopCustomerRow>(new CommandDefinition(
            """
            SELECT i.customer_id AS CustomerId, c.name AS CustomerName,
                   SUM(i.total_syp) AS TotalSyp, SUM(i.total_usd) AS TotalUsd, COUNT(*)::int AS InvoiceCount
            FROM invoices i
            INNER JOIN customers c ON c.id = i.customer_id
            WHERE i.status = 'POSTED' AND i.invoice_type = 'SALE'
              AND i.invoice_date >= date_trunc('month', CURRENT_DATE)::date
            GROUP BY i.customer_id, c.name
            ORDER BY SUM(i.total_usd) DESC
            LIMIT 5;
            """,
            cancellationToken: cancellationToken))).ToList();

        var recent = (await connection.QueryAsync<RecentRow>(new CommandDefinition(
            """
            SELECT i.id AS Id, COALESCE(i.invoice_number, '') AS InvoiceNumber, c.name AS CustomerName,
                   i.invoice_date AS InvoiceDate, i.total_syp AS TotalSyp, i.total_usd AS TotalUsd, i.status AS Status
            FROM invoices i
            INNER JOIN customers c ON c.id = i.customer_id
            WHERE i.status = 'POSTED' AND i.invoice_type = 'SALE'
            ORDER BY i.invoice_date DESC, i.created_at DESC
            LIMIT 5;
            """,
            cancellationToken: cancellationToken))).ToList();

        return Result<DashboardSummaryDto>.Success(new DashboardSummaryDto(
            money.ActiveCustomers,
            money.PostedInvoices,
            money.ReceivablesSyp,
            money.ReceivablesUsd,
            money.OverdueInvoices,
            money.OverdueSyp,
            money.SalesTodaySyp,
            money.SalesTodayUsd,
            money.SalesMonthSyp,
            money.SalesMonthUsd,
            stock.SkusInStock,
            stock.SkusOutOfStock,
            stock.SkusLowStock,
            stock.OpenAlerts,
            byDay.Select(x => new DashboardSalesDayDto(x.Day, x.TotalSyp, x.TotalUsd, x.InvoiceCount)).ToArray(),
            top.Select(x => new DashboardTopCustomerDto(x.CustomerId, x.CustomerName, x.TotalSyp, x.TotalUsd, x.InvoiceCount)).ToArray(),
            recent.Select(x => new DashboardRecentInvoiceDto(x.Id, x.InvoiceNumber, x.CustomerName, x.InvoiceDate, x.TotalSyp, x.TotalUsd, x.Status)).ToArray()));
    }

    private sealed record MoneyRow(
        int ActiveCustomers, int PostedInvoices, decimal ReceivablesSyp, decimal ReceivablesUsd, int OverdueInvoices,
        decimal OverdueSyp, decimal SalesTodaySyp, decimal SalesTodayUsd, decimal SalesMonthSyp, decimal SalesMonthUsd);

    private sealed record StockRow(int SkusInStock, int SkusOutOfStock, int SkusLowStock, int OpenAlerts);

    private sealed record SalesDayRow(DateOnly Day, decimal TotalSyp, decimal TotalUsd, int InvoiceCount);

    private sealed record TopCustomerRow(Guid CustomerId, string CustomerName, decimal TotalSyp, decimal TotalUsd, int InvoiceCount);

    private sealed record RecentRow(Guid Id, string InvoiceNumber, string CustomerName, DateOnly InvoiceDate, decimal TotalSyp, decimal TotalUsd, string Status);
}
