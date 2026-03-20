namespace AutoPartsERP.Domain.Messaging;

public sealed record InvoicePostedPayload(
    Guid InvoiceId,
    Guid CustomerId,
    decimal TotalSyp,
    decimal TotalUsd,
    DateOnly InvoiceDate,
    Guid? SalesRepId,
    int LineCount);

public sealed record PaymentAllocatedPayload(
    Guid PaymentId,
    Guid InvoiceId,
    decimal AllocatedSyp,
    decimal AllocatedUsd,
    DateOnly AllocationDate);

public sealed record InvoiceVoidedPayload(
    Guid InvoiceId,
    Guid ReversalInvoiceId,
    string Reason,
    Guid VoidedBy);

public sealed record PaymentReversedPayload(
    Guid PaymentId,
    string Reason,
    Guid ReversedBy);

public sealed record BatchReceivedPayload(
    Guid BatchId,
    Guid SkuId,
    Guid LocationId,
    decimal Quantity);

public sealed record StockAdjustedPayload(
    Guid SkuId,
    Guid LocationId,
    Guid? BatchId,
    decimal QuantityDelta,
    string Reason);

public sealed record WarrantyClaimCreatedPayload(
    Guid WarrantyRecordId,
    DateOnly ClaimDate);

public sealed record WarrantyProcessedPayload(
    Guid WarrantyRecordId,
    Guid? ReplacementSkuId,
    string Resolution);

public sealed record PartyTypeActivatedPayload(
    Guid PartyId,
    string TypeCode,
    Guid ApprovedBy);
