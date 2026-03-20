namespace AutoPartsERP.Domain.Operational;

public sealed class InventoryStock : AuditableEntity
{
    public InventoryStock(Guid id, Guid skuId, Guid locationId)
        : base(id)
    {
        SkuId = skuId;
        LocationId = locationId;
        QuantityOnHand = 0m;
        QuantityReserved = 0m;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid SkuId { get; private set; }

    public Guid LocationId { get; private set; }

    public decimal QuantityOnHand { get; private set; }

    public decimal QuantityReserved { get; private set; }

    public decimal QuantityAvailable => QuantityOnHand - QuantityReserved;

    public DateTimeOffset UpdatedAt { get; private set; }

    public Result Add(decimal quantity)
    {
        if (quantity <= 0)
        {
            return Result.Failure(new Error("Stock.InvalidQuantity", "Quantity must be greater than zero."));
        }

        QuantityOnHand += quantity;
        UpdatedAt = DateTimeOffset.UtcNow;
        Touch();
        return Result.Success();
    }

    public Result Deduct(decimal quantity)
    {
        if (quantity <= 0)
        {
            return Result.Failure(new Error("Stock.InvalidQuantity", "Quantity must be greater than zero."));
        }

        if (QuantityOnHand - quantity < 0)
        {
            return Result.Failure(new Error("Stock.InsufficientQuantity", "Insufficient stock quantity."));
        }

        QuantityOnHand -= quantity;
        UpdatedAt = DateTimeOffset.UtcNow;
        Touch();
        return Result.Success();
    }
}
