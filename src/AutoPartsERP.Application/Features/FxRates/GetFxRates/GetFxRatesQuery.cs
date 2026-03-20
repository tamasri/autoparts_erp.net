namespace AutoPartsERP.Application.Features.FxRates.GetFxRates;

public sealed record GetFxRatesQuery(FxRateQueryRequest Request)
    : IRequest<Result<PagedResponse<FxRateDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.FxRates.Read;
}

public sealed class GetFxRatesQueryValidator : AbstractValidator<GetFxRatesQuery>
{
    public GetFxRatesQueryValidator()
    {
        RuleFor(x => x.Request.PageNumber).GreaterThan(0);
        RuleFor(x => x.Request.PageSize).InclusiveBetween(1, 1000);
    }
}

public sealed class GetFxRatesQueryHandler : IRequestHandler<GetFxRatesQuery, Result<PagedResponse<FxRateDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetFxRatesQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PagedResponse<FxRateDto>>> Handle(GetFxRatesQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var total = await CountAsync(connection, "SELECT COUNT(*) FROM fx_rates;", cancellationToken);
        var offset = (request.Request.PageNumber - 1) * request.Request.PageSize;
        var rows = await QueryAsync(connection, """
            SELECT id, rate_date, currency_from, currency_to, buy_rate, sell_rate, mid_rate, is_active, notes, created_at
            FROM fx_rates
            ORDER BY rate_date DESC, created_at DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
            """,
            new Dictionary<string, object?> { ["Offset"] = offset, ["PageSize"] = request.Request.PageSize },
            cancellationToken);

        return Result<PagedResponse<FxRateDto>>.Success(new PagedResponse<FxRateDto>(rows, request.Request.PageNumber, request.Request.PageSize, total));
    }

    private static async Task<long> CountAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var scalar = await command.ExecuteScalarAsync(cancellationToken);
        return scalar is null or DBNull ? 0L : Convert.ToInt64(scalar);
    }

    private static async Task<IReadOnlyCollection<FxRateDto>> QueryAsync(DbConnection connection, string sql, IReadOnlyDictionary<string, object?> parameters, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            var dbParameter = command.CreateParameter();
            dbParameter.ParameterName = parameter.Key;
            dbParameter.Value = parameter.Value ?? DBNull.Value;
            command.Parameters.Add(dbParameter);
        }

        var items = new List<FxRateDto>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            items.Add(new FxRateDto(
                reader.GetGuid(reader.GetOrdinal("id")),
                DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("rate_date"))),
                reader.GetString(reader.GetOrdinal("currency_from")),
                reader.GetString(reader.GetOrdinal("currency_to")),
                reader.GetDecimal(reader.GetOrdinal("buy_rate")),
                reader.GetDecimal(reader.GetOrdinal("sell_rate")),
                reader.GetDecimal(reader.GetOrdinal("mid_rate")),
                reader.GetBoolean(reader.GetOrdinal("is_active")),
                reader.IsDBNull(reader.GetOrdinal("notes")) ? null : reader.GetString(reader.GetOrdinal("notes")),
                reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at"))));
        }

        return items;
    }
}
