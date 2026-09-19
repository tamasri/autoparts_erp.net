namespace AutoPartsERP.Contracts.Inventory;

/// <summary>One catalogue row for the item-picker dialog, with everything a document line needs pre-resolved.</summary>
public sealed record PickItemDto(
    Guid? ItemId,
    Guid? SkuId,
    string Code,
    string Name,
    string NameAr,
    string? Brand,
    string? Barcode,
    bool IsActive,
    bool IsStopShip,
    bool HasWarranty,
    bool IsBatchTracked,
    decimal SellingPriceSyp,
    decimal SellingPriceUsd,
    decimal MinSellingPriceSyp,
    decimal MinSellingPriceUsd,
    decimal TotalAvailable,
    IReadOnlyCollection<PickStockDto> Stock,
    IReadOnlyCollection<PickBatchDto> Batches);

public sealed record PickStockDto(
    Guid LocationId,
    string LocationCode,
    string LocationName,
    decimal Available,
    decimal Reserved);

public sealed record PickBatchDto(
    Guid Id,
    string BatchNumber,
    Guid LocationId,
    decimal Quantity,
    DateOnly? ExpiryDate);

public sealed record LocationDto(
    Guid Id,
    string Code,
    string Name,
    string Type,
    Guid? ParentId);
