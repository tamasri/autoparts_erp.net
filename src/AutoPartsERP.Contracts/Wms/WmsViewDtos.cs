namespace AutoPartsERP.Contracts.Wms;

public sealed record IssueOrderListDto(
    Guid Id,
    string OrderNo,
    string SourceType,
    Guid? SourceId,
    Guid WarehouseId,
    string Status,
    DateTimeOffset? IssuedAt,
    DateTimeOffset CreatedAt);

public sealed record IssueOrderLineViewDto(
    Guid Id,
    Guid ItemId,
    string ItemCode,
    string ItemName,
    decimal RequestedQty,
    decimal PickedQty,
    decimal VerifiedQty,
    decimal IssuedQty,
    Guid? SourceLocationId);

public sealed record PickTaskViewDto(
    Guid Id,
    Guid IssueOrderLineId,
    string ItemCode,
    string ItemName,
    Guid LocationId,
    string LocationCode,
    decimal Qty,
    string Status);

public sealed record IssueOrderDetailDto(
    IssueOrderListDto Order,
    IReadOnlyCollection<IssueOrderLineViewDto> Lines,
    IReadOnlyCollection<PickTaskViewDto> PickTasks);

public sealed record TransferRequestListDto(
    Guid Id,
    Guid SourceWarehouseId,
    Guid DestinationWarehouseId,
    string Status,
    Guid RequestedBy,
    Guid? ApprovedBy,
    string? Notes,
    DateTimeOffset CreatedAt);

public sealed record CycleCountLineViewDto(
    Guid Id,
    Guid ItemId,
    string ItemCode,
    string ItemName,
    Guid LocationId,
    string LocationCode,
    decimal SystemQty,
    decimal? CountedQty,
    decimal VarianceQty,
    string? ReasonCode,
    string? Notes);

public sealed record CycleCountPlanDetailDto(
    Guid Id,
    Guid WarehouseId,
    string ScopeType,
    string Status,
    DateOnly ScheduledFor,
    IReadOnlyCollection<CycleCountLineViewDto> Lines);

public sealed record ReceivingLineViewDto(
    Guid Id,
    Guid ItemId,
    string ItemCode,
    string ItemName,
    decimal? ExpectedQty,
    decimal ReceivedQty,
    decimal RejectedQty,
    Guid? AssignedLocationId,
    string ConditionStatus);

public sealed record ReceivingDocumentDetailDto(
    Guid Id,
    string DocumentNo,
    Guid? VendorPartyId,
    string? PurchaseOrderRef,
    Guid WarehouseId,
    string Status,
    DateTimeOffset? PostedAt,
    string? Notes,
    IReadOnlyCollection<ReceivingLineViewDto> Lines);
