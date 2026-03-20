using Dapper;
using Humanizer;

namespace AutoPartsERP.Application.Features.Invoices.GetInvoices;

public sealed record GetInvoicesQuery(
    int PageNumber = 1,
    int PageSize = 20,
    string? Status = null,
    string? Type = null,
    Guid? CustomerId = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? SearchTerm = null)
    : IRequest<Result<PagedResponse<InvoiceListItemDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Invoices.Read;
}

public sealed class GetInvoicesQueryValidator : AbstractValidator<GetInvoicesQuery>
{
    public GetInvoicesQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

public sealed class GetInvoicesQueryHandler : IRequestHandler<GetInvoicesQuery, Result<PagedResponse<InvoiceListItemDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetInvoicesQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PagedResponse<InvoiceListItemDto>>> Handle(GetInvoicesQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            conditions.Add("i.status = @Status");
            parameters.Add("Status", request.Status.Trim().ToUpperInvariant());
        }

        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            conditions.Add("i.invoice_type = @Type");
            parameters.Add("Type", request.Type.Trim().ToUpperInvariant());
        }

        if (request.CustomerId.HasValue)
        {
            conditions.Add("i.customer_id = @CustomerId");
            parameters.Add("CustomerId", request.CustomerId.Value);
        }

        if (request.FromDate.HasValue)
        {
            conditions.Add("i.invoice_date >= @FromDate");
            parameters.Add("FromDate", request.FromDate.Value);
        }

        if (request.ToDate.HasValue)
        {
            conditions.Add("i.invoice_date <= @ToDate");
            parameters.Add("ToDate", request.ToDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.SearchTerm))
        {
            conditions.Add("(i.invoice_number ILIKE @Search OR c.name ILIKE @Search OR c.code ILIKE @Search)");
            parameters.Add("Search", $"%{request.SearchTerm.Trim()}%");
        }

        var where = conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);
        parameters.Add("Offset", (request.PageNumber - 1) * request.PageSize);
        parameters.Add("PageSize", request.PageSize);

        var items = (await connection.QueryAsync<InvoiceListItemDto>(
            new CommandDefinition($"""
                SELECT
                    i.id AS Id,
                    COALESCE(i.invoice_number, '') AS InvoiceNumber,
                    i.status AS Status,
                    i.invoice_type AS Type,
                    i.customer_id AS CustomerId,
                    c.name AS CustomerName,
                    i.invoice_date AS InvoiceDate,
                    i.due_date AS DueDate,
                    i.total_syp AS TotalSyp,
                    i.total_usd AS TotalUsd,
                    i.balance_syp AS BalanceSyp,
                    i.balance_usd AS BalanceUsd,
                    i.status AS StatusDisplay,
                    i.invoice_type AS TypeDisplay,
                    i.due_date AS DueDateDisplay
                FROM invoices i
                INNER JOIN customers c ON c.id = i.customer_id
                {where}
                ORDER BY i.invoice_date DESC, i.created_at DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                """,
                parameters,
                cancellationToken: cancellationToken))).ToArray();

        var mapped = items.Select(item => item with
        {
            StatusDisplay = item.Status.Humanize(LetterCasing.Title),
            TypeDisplay = item.Type.Humanize(LetterCasing.Title),
            DueDateDisplay = InvoiceMappings.GetDueDateDisplay(item.DueDate)
        }).ToArray();

        var total = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition($"""
                SELECT COUNT(*)
                FROM invoices i
                INNER JOIN customers c ON c.id = i.customer_id
                {where};
                """,
                parameters,
                cancellationToken: cancellationToken));

        return Result<PagedResponse<InvoiceListItemDto>>.Success(new PagedResponse<InvoiceListItemDto>(mapped, request.PageNumber, request.PageSize, total));
    }
}
