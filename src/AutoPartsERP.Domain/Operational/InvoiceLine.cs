namespace AutoPartsERP.Domain.Operational;

public sealed class InvoiceLine : AuditableEntity
{
    private InvoiceLine() : base(Guid.Empty) { }

    internal InvoiceLine(
        Guid id,
        Guid invoiceId,
        int lineNumber,
        Guid skuId,
        Guid? batchId,
        Guid locationId,
        decimal quantity,
        decimal unitPriceSyp,
        decimal unitPriceUsd,
        decimal discountPct,
        decimal costPriceSyp,
        decimal costPriceUsd,
        decimal fxRateUsed,
        bool isPriceOverride,
        string? priceOverrideReason)
        : base(id)
    {
        InvoiceId = invoiceId;
        LineNumber = lineNumber;
        SkuId = skuId;
        BatchId = batchId;
        LocationId = locationId;
        Quantity = quantity;
        UnitPriceSyp = unitPriceSyp;
        UnitPriceUsd = unitPriceUsd;
        DiscountPct = discountPct;
        CostPriceSyp = costPriceSyp;
        CostPriceUsd = costPriceUsd;
        FxRateUsed = fxRateUsed;
        IsPriceOverride = isPriceOverride;
        PriceOverrideReason = priceOverrideReason;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid InvoiceId { get; private set; }

    public int LineNumber { get; private set; }

    public Guid SkuId { get; private set; }

    public Guid? BatchId { get; private set; }

    public Guid LocationId { get; private set; }

    public string? Description { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal UnitPriceSyp { get; private set; }

    public decimal UnitPriceUsd { get; private set; }

    public decimal DiscountPct { get; private set; }

    public decimal LineTotalSyp => Quantity * UnitPriceSyp * (1m - (DiscountPct / 100m));

    public decimal LineTotalUsd => Quantity * UnitPriceUsd * (1m - (DiscountPct / 100m));

    public decimal CostPriceSyp { get; private set; }

    public decimal CostPriceUsd { get; private set; }

    public decimal FxRateUsed { get; private set; }

    public bool IsPriceOverride { get; private set; }

    public string? PriceOverrideReason { get; private set; }

    public Guid? PriceOverrideApprovedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public decimal GrossMarginSyp => LineTotalSyp - (Quantity * CostPriceSyp);

    public decimal GrossMarginUsd => LineTotalUsd - (Quantity * CostPriceUsd);

    public decimal GrossMarginPct => LineTotalSyp == 0 ? 0 : (GrossMarginSyp / LineTotalSyp) * 100m;
}
