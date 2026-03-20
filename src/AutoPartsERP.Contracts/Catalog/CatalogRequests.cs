namespace AutoPartsERP.Contracts.Catalog;

public sealed record CreateCategoryRequest(
    string Name,
    string? NameAr,
    Guid? ParentId);

public sealed record CreateSkuRequest(
    string Code,
    string Name,
    string NameAr,
    Guid CategoryId,
    string? Barcode,
    decimal SellingPriceSyp,
    decimal SellingPriceUsd,
    decimal MinSellingPriceSyp,
    decimal MinSellingPriceUsd,
    bool IsBatchTracked,
    bool HasWarranty,
    int WarrantyMonths,
    string[]? Tags);

public sealed record UpdateSkuPricesRequest(
    decimal SellingPriceSyp,
    decimal SellingPriceUsd,
    decimal MinSellingPriceSyp,
    decimal MinSellingPriceUsd,
    string? OverrideReason,
    bool RequiresApproval = false);

public sealed record SkuQueryRequest(
    int PageNumber = 1,
    int PageSize = 20,
    Guid? CategoryId = null,
    bool? IsActive = null,
    bool? IsBatchTracked = null,
    bool? HasWarranty = null,
    string[]? Tags = null,
    string? SearchTerm = null);
