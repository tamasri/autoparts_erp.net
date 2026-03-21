namespace AutoPartsERP.Contracts.Items;

public sealed record SearchItemsRequest(
    string Query,
    int PageNumber = 1,
    int PageSize = 20,
    bool IncludeInactive = false);

public sealed record CreateItemRequest(
    Guid? SkuId,
    string PartNumber,
    string NameEn,
    string NameAr,
    string? NameArColloquial,
    string? Brand,
    string? CategoryPath,
    bool HasWarranty,
    int WarrantyMonths,
    bool IsBatchTracked,
    decimal ReorderLevel,
    string? Notes);

public sealed record UpdateItemRequest(
    string NameEn,
    string NameAr,
    string? NameArColloquial,
    string? Brand,
    string? CategoryPath,
    bool IsActive,
    bool HasWarranty,
    int WarrantyMonths,
    bool IsBatchTracked,
    decimal ReorderLevel,
    string? Notes);

public sealed record AddItemAliasRequest(
    string Alias,
    string Source = "MANUAL");

public sealed record AddItemInterchangeRequest(
    Guid InterchangeItemId,
    string Type,
    int Priority = 1,
    string? Notes = null);

public sealed record MarkItemStopShipRequest(
    string Reason);

