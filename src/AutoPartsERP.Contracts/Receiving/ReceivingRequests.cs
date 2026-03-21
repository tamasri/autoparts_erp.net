namespace AutoPartsERP.Contracts.Receiving;

public sealed record CreateReceivingDocumentRequest(
    Guid? VendorPartyId,
    string? PurchaseOrderRef,
    Guid WarehouseId,
    string? Notes);

public sealed record AddReceivingLineRequest(
    Guid ItemId,
    decimal? ExpectedQty,
    decimal ReceivedQty,
    decimal RejectedQty,
    Guid? BatchId,
    Guid? AssignedLocationId,
    string ConditionStatus,
    bool? ManufacturerPartMatchOk,
    string? Notes);

public sealed record CompletePutawayTaskRequest(
    Guid ToLocationId,
    decimal Qty);

