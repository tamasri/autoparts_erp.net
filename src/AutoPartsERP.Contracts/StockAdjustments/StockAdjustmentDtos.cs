namespace AutoPartsERP.Contracts.StockAdjustments;

public sealed record StockAdjustmentDto(
    Guid Id,
    string AdjustmentNo,
    string AdjustmentType,
    Guid WarehouseId,
    string ReasonCode,
    string Status,
    DateTimeOffset? PostedAt,
    IReadOnlyCollection<StockAdjustmentLineDto> Lines);

public sealed record StockAdjustmentLineDto(
    Guid Id,
    Guid StockAdjustmentId,
    Guid ItemId,
    Guid LocationId,
    Guid? BatchId,
    string Status,
    decimal QtyDelta,
    decimal SystemQtyBefore,
    decimal SystemQtyAfter,
    string? Notes);

