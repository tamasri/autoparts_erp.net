namespace AutoPartsERP.Contracts.Catalog;

public sealed record CategoryDto(
    Guid Id,
    string Path,
    string Name,
    string? NameAr,
    Guid? ParentId,
    int Depth,
    bool IsActive);

public sealed record SkuDto(
    Guid Id,
    string Code,
    string Name,
    string NameAr,
    Guid CategoryId,
    string? Barcode,
    bool IsBatchTracked,
    bool HasWarranty,
    int WarrantyMonths,
    decimal SellingPriceSyp,
    decimal SellingPriceUsd,
    decimal MinSellingPriceSyp,
    decimal MinSellingPriceUsd,
    bool IsActive,
    string[] Tags);
