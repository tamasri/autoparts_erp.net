namespace AutoPartsERP.Contracts.FxRates;

public sealed record FxRateDto(
    Guid Id,
    DateOnly RateDate,
    string CurrencyFrom,
    string CurrencyTo,
    decimal BuyRate,
    decimal SellRate,
    decimal MidRate,
    bool IsActive,
    string? Notes,
    DateTimeOffset CreatedAt);
