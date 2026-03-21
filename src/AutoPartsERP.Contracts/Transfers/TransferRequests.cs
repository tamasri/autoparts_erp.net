namespace AutoPartsERP.Contracts.Transfers;

public sealed record CreateTransferOrderRequest(
    Guid SourceWarehouseId,
    Guid DestinationWarehouseId,
    IReadOnlyCollection<CreateTransferOrderLineRequest> Lines);

public sealed record CreateTransferOrderLineRequest(
    Guid ItemId,
    Guid? BatchId,
    Guid? SourceLocationId,
    Guid? DestinationLocationId,
    decimal ShippedQty);

