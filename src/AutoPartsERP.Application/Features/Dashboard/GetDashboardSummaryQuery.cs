using AutoPartsERP.Contracts.Dashboard;

namespace AutoPartsERP.Application.Features.Dashboard;

/// <summary>
/// Today's work on the home screen: the latest posted invoices and the open stock alerts. The business figures, with their filters, come from
/// <see cref="GetBusinessKpisQuery"/> so no figure is computed in two places.
/// </summary>
public sealed record GetDashboardSummaryQuery()
    : IRequest<Result<DashboardSummaryDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Invoices.Read;
}

public sealed class GetDashboardSummaryQueryHandler : IRequestHandler<GetDashboardSummaryQuery, Result<DashboardSummaryDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetDashboardSummaryQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<DashboardSummaryDto>> Handle(GetDashboardSummaryQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var openAlerts = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*)::int FROM inventory_alerts WHERE status IN ('OPEN', 'ACKNOWLEDGED');", cancellationToken: cancellationToken));

        var recent = (await connection.QueryAsync<DashboardRecentInvoiceDto>(new CommandDefinition(
            """
            SELECT i.id AS Id, COALESCE(i.invoice_number, '') AS InvoiceNumber, c.name AS CustomerName,
                   i.invoice_date AS InvoiceDate, i.total_syp AS TotalSyp, i.total_usd AS TotalUsd, i.status AS Status
            FROM invoices i
            INNER JOIN customers c ON c.id = i.customer_id
            WHERE i.status = 'POSTED' AND i.invoice_type = 'SALE'
            ORDER BY i.invoice_date DESC, i.created_at DESC
            LIMIT 5;
            """,
            cancellationToken: cancellationToken))).ToArray();

        return Result<DashboardSummaryDto>.Success(new DashboardSummaryDto(openAlerts, recent));
    }
}
