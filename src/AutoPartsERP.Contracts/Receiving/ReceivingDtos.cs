namespace AutoPartsERP.Contracts.Receiving;

public sealed record ReceivingDocumentDto(
    Guid Id,
    string DocumentNo,
    Guid? VendorPartyId,
    string? PurchaseOrderRef,
    Guid WarehouseId,
    string Status,
    Guid ReceivedBy,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset? PostedAt,
    string? Notes,
    IReadOnlyCollection<ReceivingLineDto> Lines);

public sealed record ReceivingLineDto(
    Guid Id,
    Guid ReceivingDocumentId,
    Guid ItemId,
    decimal? ExpectedQty,
    decimal ReceivedQty,
    decimal RejectedQty,
    Guid? BatchId,
    Guid? AssignedLocationId,
    string ConditionStatus,
    bool? ManufacturerPartMatchOk,
    string? Notes);

public sealed record PutawayTaskDto(
    Guid Id,
    Guid ReceivingLineId,
    Guid FromLocationId,
    Guid ToLocationId,
    decimal Qty,
    string Status,
    Guid? AssignedTo,
    Guid? ConfirmedBy,
    DateTimeOffset? ConfirmedAt);

