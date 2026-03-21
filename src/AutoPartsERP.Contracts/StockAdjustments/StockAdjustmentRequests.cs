namespace AutoPartsERP.Contracts.StockAdjustments;

public sealed record CreateStockAdjustmentRequest(
    string AdjustmentType,
    Guid WarehouseId,
    string ReasonCode,
    IReadOnlyCollection<CreateStockAdjustmentLineRequest> Lines);

public sealed record CreateStockAdjustmentLineRequest(
    Guid ItemId,
    Guid LocationId,
    Guid? BatchId,
    string Status,
    decimal QtyDelta,
    decimal SystemQtyBefore,
    decimal SystemQtyAfter,
    string? Notes);

