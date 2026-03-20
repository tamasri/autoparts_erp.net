namespace AutoPartsERP.Contracts.Warranty;

public sealed record WarrantyRecordDto(
    Guid Id,
    string WarrantyNumber,
    Guid InvoiceLineId,
    Guid SkuId,
    string SkuCode,
    string SkuName,
    Guid? BatchId,
    Guid CustomerId,
    string CustomerName,
    DateOnly SaleDate,
    DateOnly ExpiryDate,
    DateOnly? ClaimDate,
    string Status,
    string? ClaimDescription,
    string? Resolution,
    string? RejectionReason,
    string StatusDisplay,
    string WarrantyExpiryDisplay);
