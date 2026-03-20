namespace AutoPartsERP.Application.Features.FxRates.CreateFxRate;

public sealed record CreateFxRateCommand(CreateFxRateRequest Request, string IdempotencyKey)
    : IRequest<Result<FxRateDto>>, IAuthorizedRequest, IIdempotentRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.FxRates.Manage;
    public string AuditModule => "FX_RATES";
}

public sealed class CreateFxRateCommandValidator : AbstractValidator<CreateFxRateCommand>
{
    public CreateFxRateCommandValidator()
    {
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.Request.RateDate).NotEmpty();
        RuleFor(x => x.Request.BuyRate).GreaterThan(0);
        RuleFor(x => x.Request.SellRate).GreaterThan(0);
        RuleFor(x => x.Request.BuyRate).GreaterThanOrEqualTo(x => x.Request.SellRate);
        RuleFor(x => x.Request.RateDate).Must(date => date <= DateOnly.FromDateTime(DateTime.UtcNow));
    }
}

public sealed class CreateFxRateCommandHandler : IRequestHandler<CreateFxRateCommand, Result<FxRateDto>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public CreateFxRateCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<FxRateDto>> Handle(CreateFxRateCommand request, CancellationToken cancellationToken)
    {
        var requestDate = request.Request.RateDate;
        if (requestDate > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return Result<FxRateDto>.Failure(new Error("FxRate.FutureDate", "FX rate date cannot be in the future."));
        }

        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        await using (var existsCommand = connection.CreateCommand())
        {
            existsCommand.CommandText = """
                SELECT 1
                FROM fx_rates
                WHERE rate_date = @RateDate
                  AND currency_from = @CurrencyFrom
                  AND currency_to = @CurrencyTo;
                """;
            AddParameter(existsCommand, "RateDate", requestDate);
            AddParameter(existsCommand, "CurrencyFrom", request.Request.CurrencyFrom.Trim().ToUpperInvariant());
            AddParameter(existsCommand, "CurrencyTo", request.Request.CurrencyTo.Trim().ToUpperInvariant());

            var exists = await existsCommand.ExecuteScalarAsync(cancellationToken);
            if (exists is not null)
            {
                return Result<FxRateDto>.Failure(new Error("FxRate.DuplicateRate", "An FX rate already exists for that date."));
            }
        }

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO fx_rates
                (id, rate_date, currency_from, currency_to, buy_rate, sell_rate, is_active, notes, created_by)
            VALUES
                (@Id, @RateDate, @CurrencyFrom, @CurrencyTo, @BuyRate, @SellRate, TRUE, @Notes, @CreatedBy)
            RETURNING id, rate_date, currency_from, currency_to, buy_rate, sell_rate, mid_rate, is_active, notes, created_at;
            """;
        AddParameter(command, "Id", Guid.NewGuid());
        AddParameter(command, "RateDate", requestDate);
        AddParameter(command, "CurrencyFrom", request.Request.CurrencyFrom.Trim().ToUpperInvariant());
        AddParameter(command, "CurrencyTo", request.Request.CurrencyTo.Trim().ToUpperInvariant());
        AddParameter(command, "BuyRate", request.Request.BuyRate);
        AddParameter(command, "SellRate", request.Request.SellRate);
        AddParameter(command, "Notes", (object?)request.Request.Notes?.Trim() ?? DBNull.Value);
        AddParameter(command, "CreatedBy", _currentUser.UserId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return Result<FxRateDto>.Failure(new Error("FxRate.CreateFailed", "FX rate could not be created."));
        }

        return Result<FxRateDto>.Success(MapFxRate(reader));
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    private static FxRateDto MapFxRate(DbDataReader reader)
    {
        return new FxRateDto(
            reader.GetGuid(reader.GetOrdinal("id")),
            DateOnly.FromDateTime(reader.GetDateTime(reader.GetOrdinal("rate_date"))),
            reader.GetString(reader.GetOrdinal("currency_from")),
            reader.GetString(reader.GetOrdinal("currency_to")),
            reader.GetDecimal(reader.GetOrdinal("buy_rate")),
            reader.GetDecimal(reader.GetOrdinal("sell_rate")),
            reader.GetDecimal(reader.GetOrdinal("mid_rate")),
            reader.GetBoolean(reader.GetOrdinal("is_active")),
            reader.IsDBNull(reader.GetOrdinal("notes")) ? null : reader.GetString(reader.GetOrdinal("notes")),
            reader.GetFieldValue<DateTimeOffset>(reader.GetOrdinal("created_at")));
    }
}
