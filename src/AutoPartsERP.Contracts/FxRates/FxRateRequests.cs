namespace AutoPartsERP.Contracts.FxRates;

public sealed record CreateFxRateRequest(
    DateOnly RateDate,
    decimal BuyRate,
    decimal SellRate,
    string CurrencyFrom = "USD",
    string CurrencyTo = "SYP",
    string? Notes = null);

public sealed record FxRateQueryRequest(
    int PageNumber = 1,
    int PageSize = 20);
