namespace AutoPartsERP.Contracts.Transfers;

public sealed record TransferOrderDto(
    Guid Id,
    string TransferNo,
    Guid? TransferRequestId,
    Guid SourceWarehouseId,
    Guid DestinationWarehouseId,
    string Status,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? ReceivedAt,
    IReadOnlyCollection<TransferOrderLineDto> Lines);

public sealed record TransferOrderLineDto(
    Guid Id,
    Guid TransferOrderId,
    Guid ItemId,
    Guid? BatchId,
    Guid? SourceLocationId,
    Guid? DestinationLocationId,
    decimal ShippedQty,
    decimal ReceivedQty);

