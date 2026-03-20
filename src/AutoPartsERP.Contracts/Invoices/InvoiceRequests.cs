namespace AutoPartsERP.Contracts.Invoices;

public sealed record CreateInvoiceLineRequest(
    Guid SkuId,
    Guid? BatchId,
    Guid LocationId,
    decimal Quantity,
    decimal UnitPriceSyp,
    decimal UnitPriceUsd,
    decimal DiscountPct,
    bool IsPriceOverride,
    string? OverrideReason);

public sealed record CreateInvoiceRequest(
    Guid CustomerId,
    DateOnly InvoiceDate,
    DateOnly DueDate,
    Guid FxRateId,
    string InvoiceType,
    Guid? SalesRepId,
    decimal DeliveryFeeSyp,
    decimal DeliveryFeeUsd,
    IReadOnlyCollection<CreateInvoiceLineRequest> Lines);

public sealed record AddInvoiceLineRequest(
    Guid InvoiceId,
    CreateInvoiceLineRequest Line);

public sealed record UpdateDeliveryFeeRequest(
    decimal DeliveryFeeSyp,
    decimal DeliveryFeeUsd);

public sealed record VoidInvoiceRequest(
    string Reason);

public sealed record InvoiceQueryRequest(
    int PageNumber = 1,
    int PageSize = 20,
    string? Status = null,
    string? Type = null,
    Guid? CustomerId = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? SearchTerm = null);
