namespace AutoPartsERP.Domain.Operational;

public sealed class FxRate : AuditableEntity
{
    private FxRate(
        Guid id,
        DateOnly rateDate,
        string currencyFrom,
        string currencyTo,
        decimal buyRate,
        decimal sellRate,
        Guid createdBy)
        : base(id)
    {
        RateDate = rateDate;
        CurrencyFrom = currencyFrom;
        CurrencyTo = currencyTo;
        BuyRate = buyRate;
        SellRate = sellRate;
        IsActive = true;
        CreatedBy = createdBy;
    }

    public DateOnly RateDate { get; private set; }

    public string CurrencyFrom { get; private set; } = "USD";

    public string CurrencyTo { get; private set; } = "SYP";

    public decimal BuyRate { get; private set; }

    public decimal SellRate { get; private set; }

    public decimal MidRate => (BuyRate + SellRate) / 2m;

    public bool IsActive { get; private set; }

    public string? Notes { get; private set; }

    public Guid CreatedBy { get; private set; }

    public static Result<FxRate> Create(
        DateOnly rateDate,
        decimal buyRate,
        decimal sellRate,
        Guid createdBy,
        string currencyFrom = "USD",
        string currencyTo = "SYP")
    {
        if (buyRate <= 0 || sellRate <= 0)
        {
            return Result<FxRate>.Failure(new Error("FxRate.InvalidValue", "Buy and sell rates must be greater than zero."));
        }

        if (buyRate < sellRate)
        {
            return Result<FxRate>.Failure(new Error("FxRate.InvalidSpread", "Buy rate must be greater than or equal to sell rate."));
        }

        if (rateDate > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return Result<FxRate>.Failure(new Error("FxRate.FutureDate", "FX rate date cannot be in the future."));
        }

        return Result<FxRate>.Success(new FxRate(
            Guid.NewGuid(),
            rateDate,
            currencyFrom.Trim().ToUpperInvariant(),
            currencyTo.Trim().ToUpperInvariant(),
            buyRate,
            sellRate,
            createdBy));
    }

    public void SetActive(bool isActive)
    {
        IsActive = isActive;
        Touch();
    }

    public void SetNotes(string? notes)
    {
        Notes = notes?.Trim();
        Touch();
    }
}
