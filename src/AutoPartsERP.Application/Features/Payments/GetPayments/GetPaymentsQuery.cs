using Dapper;

namespace AutoPartsERP.Application.Features.Payments.GetPayments;

public sealed record GetPaymentsQuery(
    int PageNumber = 1,
    int PageSize = 20,
    Guid? CustomerId = null,
    string? PaymentMethod = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null)
    : IRequest<Result<PagedResponse<PaymentDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Payments.Read;
}

public sealed class GetPaymentsQueryValidator : AbstractValidator<GetPaymentsQuery>
{
    public GetPaymentsQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThan(0);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
    }
}

public sealed class GetPaymentsQueryHandler : IRequestHandler<GetPaymentsQuery, Result<PagedResponse<PaymentDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetPaymentsQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PagedResponse<PaymentDto>>> Handle(GetPaymentsQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var conditions = new List<string>();
        var parameters = new DynamicParameters();

        if (request.CustomerId.HasValue)
        {
            conditions.Add("p.customer_id = @CustomerId");
            parameters.Add("CustomerId", request.CustomerId.Value);
        }

        if (!string.IsNullOrWhiteSpace(request.PaymentMethod))
        {
            conditions.Add("p.payment_method = @PaymentMethod");
            parameters.Add("PaymentMethod", request.PaymentMethod.Trim().ToUpperInvariant());
        }

        if (request.FromDate.HasValue)
        {
            conditions.Add("p.payment_date >= @FromDate");
            parameters.Add("FromDate", request.FromDate.Value);
        }

        if (request.ToDate.HasValue)
        {
            conditions.Add("p.payment_date <= @ToDate");
            parameters.Add("ToDate", request.ToDate.Value);
        }

        var where = conditions.Count == 0 ? string.Empty : "WHERE " + string.Join(" AND ", conditions);
        parameters.Add("Offset", (request.PageNumber - 1) * request.PageSize);
        parameters.Add("PageSize", request.PageSize);

        var items = (await connection.QueryAsync<PaymentDto>(
            new CommandDefinition($"""
                {PaymentMappings.Select}
                {where}
                ORDER BY p.payment_date DESC, p.created_at DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                """,
                parameters,
                cancellationToken: cancellationToken))).ToArray();

        var mapped = items.Select(PaymentMappings.WithDisplay).ToArray();

        var total = await connection.ExecuteScalarAsync<int>(
            new CommandDefinition($"""
                SELECT COUNT(*)
                FROM payments p
                INNER JOIN customers c ON c.id = p.customer_id
                {where};
                """,
                parameters,
                cancellationToken: cancellationToken));

        return Result<PagedResponse<PaymentDto>>.Success(new PagedResponse<PaymentDto>(mapped, request.PageNumber, request.PageSize, total));
    }
}
