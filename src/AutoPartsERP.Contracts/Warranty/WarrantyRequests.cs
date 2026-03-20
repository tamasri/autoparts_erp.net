namespace AutoPartsERP.Contracts.Warranty;

public sealed record ClaimWarrantyRequest(
    string Description,
    DateOnly ClaimDate);

public sealed record ProcessWarrantyRequest(
    string Resolution,
    Guid? ReplacementSkuId);

public sealed record RejectWarrantyRequest(
    string Reason);

public sealed record WarrantyQueryRequest(
    int PageNumber = 1,
    int PageSize = 20,
    string? Status = null,
    Guid? CustomerId = null,
    Guid? SkuId = null);
