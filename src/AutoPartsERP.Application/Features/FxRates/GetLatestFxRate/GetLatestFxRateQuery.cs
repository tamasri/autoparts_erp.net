namespace AutoPartsERP.Application.Features.FxRates.GetLatestFxRate;

public sealed record GetLatestFxRateQuery()
    : IRequest<Result<FxRateDto>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.FxRates.Read;
}

public sealed class GetLatestFxRateQueryValidator : AbstractValidator<GetLatestFxRateQuery>
{
}

public sealed class GetLatestFxRateQueryHandler : IRequestHandler<GetLatestFxRateQuery, Result<FxRateDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetLatestFxRateQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<FxRateDto>> Handle(GetLatestFxRateQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, rate_date, currency_from, currency_to, buy_rate, sell_rate, mid_rate, is_active, notes, created_at
            FROM fx_rates
            WHERE is_active = TRUE
            ORDER BY rate_date DESC, created_at DESC
            LIMIT 1;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return Result<FxRateDto>.Failure(new Error("FxRate.NotFound", "FX rate was not found."));
        }

        return Result<FxRateDto>.Success(new FxRateDto(
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
}
