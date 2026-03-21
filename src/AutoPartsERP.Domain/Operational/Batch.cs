using AutoPartsERP.Domain.Operational.Enums;

namespace AutoPartsERP.Domain.Operational;

public sealed class Batch : AuditableEntity
{
    private Batch() : base(Guid.Empty) { }

    private Batch(
        Guid id,
        string batchNumber,
        Guid skuId,
        Guid locationId,
        decimal quantity,
        decimal costPriceSyp,
        decimal costPriceUsd,
        Guid fxRateId,
        DateOnly receivedDate,
        Guid createdBy)
        : base(id)
    {
        BatchNumber = batchNumber;
        SkuId = skuId;
        LocationId = locationId;
        QuantityInitial = quantity;
        QuantityCurrent = quantity;
        CostPriceSyp = costPriceSyp;
        CostPriceUsd = costPriceUsd;
        FxRateId = fxRateId;
        ReceivedDate = receivedDate;
        Status = BatchStatus.Active;
        CreatedBy = createdBy;
    }

    public string BatchNumber { get; private set; } = string.Empty;

    public Guid SkuId { get; private set; }

    public Guid LocationId { get; private set; }

    public decimal QuantityInitial { get; private set; }

    public decimal QuantityCurrent { get; private set; }

    public decimal CostPriceSyp { get; private set; }

    public decimal CostPriceUsd { get; private set; }

    public Guid FxRateId { get; private set; }

    public string? SupplierName { get; private set; }

    public string? SupplierInvoice { get; private set; }

    public DateOnly ReceivedDate { get; private set; }

    public DateOnly? ExpiryDate { get; private set; }

    public BatchStatus Status { get; private set; }

    public string? Notes { get; private set; }

    public Guid CreatedBy { get; private set; }

    public static Result<Batch> Create(
        string batchNumber,
        Guid skuId,
        Guid locationId,
        decimal quantity,
        decimal costPriceSyp,
        decimal costPriceUsd,
        Guid fxRateId,
        DateOnly receivedDate,
        Guid createdBy)
    {
        if (string.IsNullOrWhiteSpace(batchNumber))
        {
            return Result<Batch>.Failure(new Error("Batch.NumberRequired", "Batch number is required."));
        }

        if (quantity <= 0)
        {
            return Result<Batch>.Failure(new Error("Batch.InvalidQuantity", "Batch quantity must be greater than zero."));
        }

        return Result<Batch>.Success(new Batch(
            Guid.NewGuid(),
            batchNumber.Trim().ToUpperInvariant(),
            skuId,
            locationId,
            quantity,
            costPriceSyp,
            costPriceUsd,
            fxRateId,
            receivedDate,
            createdBy));
    }

    public Result Deduct(decimal qty)
    {
        if (qty <= 0)
        {
            return Result.Failure(new Error("Batch.InvalidQuantity", "Quantity must be greater than zero."));
        }

        if (qty > QuantityCurrent)
        {
            return Result.Failure(new Error("Stock.InsufficientQuantity", "Insufficient batch quantity."));
        }

        QuantityCurrent -= qty;
        if (QuantityCurrent == 0)
        {
            MarkDepleted();
        }

        Touch();
        return Result.Success();
    }

    public void AddToQuantity(decimal qty)
    {
        if (qty <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(qty));
        }

        QuantityCurrent += qty;
        if (Status == BatchStatus.Depleted && QuantityCurrent > 0)
        {
            Status = BatchStatus.Active;
        }

        Touch();
    }

    public void MarkDepleted()
    {
        if (QuantityCurrent < 0)
        {
            throw new InvalidOperationException("Batch quantity cannot be negative.");
        }

        if (QuantityCurrent == 0)
        {
            Status = BatchStatus.Depleted;
            Touch();
        }
    }
}
