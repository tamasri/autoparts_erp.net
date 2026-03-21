namespace AutoPartsERP.Contracts.Items;

public sealed record ItemSearchResultDto(
    Guid Id,
    string PartNumber,
    string PartNumberCanonical,
    string PartNumberNumeric,
    string NameEn,
    string NameAr,
    string? NameArColloquial,
    string? Brand,
    bool IsStopShip,
    int SortBucket,
    IReadOnlyCollection<ItemWarehouseStockDto> StockByWarehouse);

public sealed record ItemWarehouseStockDto(
    string Warehouse,
    decimal AvailableQty,
    decimal ReservedQty,
    decimal? LastMovementDaysAgo);

public sealed record ItemDto(
    Guid Id,
    Guid? SkuId,
    string PartNumber,
    string PartNumberCanonical,
    string PartNumberNumeric,
    string NameEn,
    string NameAr,
    string? NameArColloquial,
    string? Brand,
    string? CategoryPath,
    bool HasWarranty,
    int WarrantyMonths,
    bool IsBatchTracked,
    decimal ReorderLevel,
    bool IsActive,
    bool IsStopShip,
    string? StopShipReason,
    string? Notes);

public sealed record ItemAliasDto(
    Guid Id,
    Guid ItemId,
    string Alias,
    string AliasCanonical,
    string Source,
    DateTimeOffset CreatedAt);

public sealed record ItemInterchangeDto(
    Guid Id,
    Guid ItemId,
    Guid InterchangeItemId,
    string InterchangePartNumber,
    string InterchangeNameAr,
    string Type,
    int Priority,
    bool IsActive);

