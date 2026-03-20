namespace AutoPartsERP.Contracts.Inventory;

public sealed record InventoryStockDto(
    Guid Id,
    Guid SkuId,
    string SkuCode,
    string SkuName,
    Guid LocationId,
    string LocationCode,
    decimal QuantityOnHand,
    decimal QuantityReserved,
    decimal QuantityAvailable,
    bool LowStockFlag,
    string QuantityDisplay);

public sealed record BatchDto(
    Guid Id,
    string BatchNumber,
    Guid SkuId,
    Guid LocationId,
    decimal QuantityInitial,
    decimal QuantityCurrent,
    decimal CostPriceSyp,
    decimal CostPriceUsd,
    DateOnly ReceivedDate,
    DateOnly? ExpiryDate,
    string Status,
    string BatchAgeDisplay,
    string ExpiryDisplay);

public sealed record BatchMovementDto(
    Guid Id,
    Guid BatchId,
    string MovementType,
    decimal Quantity,
    string Direction,
    string? ReferenceType,
    Guid? ReferenceId,
    DateTimeOffset CreatedAt,
    string MovementTypeDisplay,
    string TimeAgo);
