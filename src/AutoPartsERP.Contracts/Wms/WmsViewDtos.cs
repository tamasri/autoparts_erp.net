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

public sealed record StockMovementDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    Guid ItemId,
    string ItemCode,
    string ItemName,
    Guid LocationId,
    string LocationCode,
    string MovementType,
    string Direction,
    decimal Qty,
    decimal? BalanceAfter,
    string? ReferenceType,
    Guid? ReferenceId,
    string? PerformedBy,
    string? Notes);

public sealed record LocationOverviewDto(
    Guid Id,
    string Code,
    string Name,
    string Type,
    Guid? ParentId,
    bool IsActive,
    int SkuCount,
    decimal TotalQty,
    decimal ValueUsd,
    int ChildCount);

public sealed record TransferLineViewDto(
    Guid Id,
    Guid ItemId,
    string ItemCode,
    string ItemName,
    Guid? SourceLocationId,
    Guid? DestinationLocationId,
    decimal ShippedQty,
    decimal ReceivedQty);

public sealed record TransferOrderDetailDto(
    Guid Id,
    string TransferNo,
    Guid SourceWarehouseId,
    Guid DestinationWarehouseId,
    string Status,
    DateTimeOffset? ShippedAt,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyCollection<TransferLineViewDto> Lines);

public sealed record StockAdjustmentLineViewDto(
    Guid Id,
    Guid ItemId,
    string ItemCode,
    string ItemName,
    Guid LocationId,
    decimal QtyDelta,
    decimal SystemQtyBefore,
    decimal SystemQtyAfter,
    string? Notes);

public sealed record StockAdjustmentDetailDto(
    Guid Id,
    string AdjustmentNo,
    string AdjustmentType,
    Guid WarehouseId,
    string ReasonCode,
    string Status,
    DateTimeOffset? PostedAt,
    DateTimeOffset CreatedAt,
    IReadOnlyCollection<StockAdjustmentLineViewDto> Lines);
