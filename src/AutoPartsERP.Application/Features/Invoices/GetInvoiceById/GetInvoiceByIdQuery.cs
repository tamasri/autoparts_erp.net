using Dapper;

namespace AutoPartsERP.Application.Features.Invoices.GetInvoiceById;

public sealed record GetInvoiceByIdQuery(Guid InvoiceId)
    : IRequest<Result<InvoiceDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Invoices.Read;
}

public sealed class GetInvoiceByIdQueryValidator : AbstractValidator<GetInvoiceByIdQuery>
{
    public GetInvoiceByIdQueryValidator()
    {
        RuleFor(x => x.InvoiceId).NotEmpty();
    }
}

public sealed class GetInvoiceByIdQueryHandler : IRequestHandler<GetInvoiceByIdQuery, Result<InvoiceDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetInvoiceByIdQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<InvoiceDto>> Handle(GetInvoiceByIdQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var header = await connection.QuerySingleOrDefaultAsync(
            new CommandDefinition(
                """
                SELECT
                    i.id AS Id,
                    COALESCE(i.invoice_number, '') AS InvoiceNumber,
                    i.status AS Status,
                    i.invoice_type AS Type,
                    i.customer_id AS CustomerId,
                    c.code AS CustomerCode,
                    c.name AS CustomerName,
                    i.invoice_date AS InvoiceDate,
                    i.due_date AS DueDate,
                    i.total_syp AS TotalSyp,
                    i.total_usd AS TotalUsd,
                    i.paid_syp AS PaidSyp,
                    i.paid_usd AS PaidUsd
                FROM invoices i
                INNER JOIN customers c ON c.id = i.customer_id
                WHERE i.id = @InvoiceId;
                """,
                new { request.InvoiceId },
                cancellationToken: cancellationToken));

        if (header is null)
        {
            return Result<InvoiceDto>.Failure(new Error("Invoice.NotFound", "Invoice was not found."));
        }

        var lines = (await connection.QueryAsync<InvoiceLineDto>(
            new CommandDefinition(
                """
                SELECT
                    il.id AS Id,
                    il.line_number AS LineNumber,
                    il.sku_id AS SkuId,
                    s.code AS SkuCode,
                    s.name AS SkuName,
                    il.batch_id AS BatchId,
                    il.location_id AS LocationId,
                    il.quantity AS Quantity,
                    il.unit_price_syp AS UnitPriceSyp,
                    il.unit_price_usd AS UnitPriceUsd,
                    il.discount_pct AS DiscountPct,
                    il.line_total_syp AS LineTotalSyp,
                    il.line_total_usd AS LineTotalUsd,
                    il.quantity * il.cost_price_syp AS GrossMarginSyp,
                    il.quantity * il.cost_price_usd AS GrossMarginUsd,
                    CASE WHEN il.line_total_syp = 0 THEN 0 ELSE ((il.line_total_syp - (il.quantity * il.cost_price_syp)) / il.line_total_syp) * 100 END AS GrossMarginPct,
                    il.is_price_override AS IsPriceOverride,
                    il.price_override_reason AS OverrideReason
                FROM invoice_lines il
                INNER JOIN skus s ON s.id = il.sku_id
                WHERE il.invoice_id = @InvoiceId
                ORDER BY il.line_number;
                """,
                new { request.InvoiceId },
                cancellationToken: cancellationToken))).ToArray();

        return Result<InvoiceDto>.Success(InvoiceMappings.ToInvoiceDto(
            header.id,
            header.invoicenumber ?? string.Empty,
            header.status,
            header.type,
            header.customerid,
            header.customercode,
            header.customername,
            header.invoicedate,
            header.duedate,
            header.totalsyp,
            header.totalusd,
            header.paidsyp,
            header.paidusd,
            lines));
    }
}
