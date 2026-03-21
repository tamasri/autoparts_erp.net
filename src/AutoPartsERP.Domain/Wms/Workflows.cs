namespace AutoPartsERP.Domain.Wms;

public sealed class ReceivingDocument : AuditableEntity
{
    private readonly List<ReceivingLine> _lines = new();

    public ReceivingDocument(Guid id, string documentNo, Guid? vendorPartyId, string? purchaseOrderRef, Guid warehouseId, Guid receivedBy, Guid createdBy)
        : base(id)
    {
        DocumentNo = documentNo;
        VendorPartyId = vendorPartyId;
        PurchaseOrderRef = purchaseOrderRef;
        WarehouseId = warehouseId;
        Status = "DRAFT";
        ReceivedBy = receivedBy;
        CreatedBy = createdBy;
    }

    private ReceivingDocument()
        : base(Guid.NewGuid())
    {
    }

    public string DocumentNo { get; private set; } = string.Empty;

    public Guid? VendorPartyId { get; private set; }

    public string? PurchaseOrderRef { get; private set; }

    public Guid WarehouseId { get; private set; }

    public string Status { get; private set; } = "DRAFT";

    public Guid ReceivedBy { get; private set; }

    public DateTimeOffset? ReceivedAt { get; private set; }

    public DateTimeOffset? PostedAt { get; private set; }

    public string? Notes { get; private set; }

    public Guid CreatedBy { get; private set; }

    public IReadOnlyCollection<ReceivingLine> Lines => _lines.AsReadOnly();

    public Result<ReceivingLine> AddLine(Guid itemId, decimal? expectedQty, decimal receivedQty, decimal rejectedQty, Guid? batchId, Guid? assignedLocationId, string conditionStatus, string? notes)
    {
        if (Status is "COMPLETED" or "CANCELLED")
        {
            return Result<ReceivingLine>.Failure(new Error("Receiving.InvalidState", "Cannot modify receiving lines in current state."));
        }

        var line = new ReceivingLine(
            Guid.NewGuid(),
            Id,
            itemId,
            expectedQty,
            receivedQty,
            rejectedQty,
            batchId,
            assignedLocationId,
            conditionStatus,
            notes);

        _lines.Add(line);
        Touch();
        return Result<ReceivingLine>.Success(line);
    }

    public Result Post(Guid by)
    {
        if (!_lines.Any())
        {
            return Result.Failure(new Error("Receiving.NoLines", "Receiving document must have at least one line."));
        }

        Status = "COMPLETED";
        PostedAt = DateTimeOffset.UtcNow;
        ReceivedAt = DateTimeOffset.UtcNow;
        Touch();
        return Result.Success();
    }
}

public sealed class ReceivingLine : AuditableEntity
{
    public ReceivingLine(
        Guid id,
        Guid receivingDocumentId,
        Guid itemId,
        decimal? expectedQty,
        decimal receivedQty,
        decimal rejectedQty,
        Guid? batchId,
        Guid? assignedLocationId,
        string conditionStatus,
        string? notes)
        : base(id)
    {
        ReceivingDocumentId = receivingDocumentId;
        ItemId = itemId;
        ExpectedQty = expectedQty;
        ReceivedQty = receivedQty;
        RejectedQty = rejectedQty;
        BatchId = batchId;
        AssignedLocationId = assignedLocationId;
        ConditionStatus = string.IsNullOrWhiteSpace(conditionStatus) ? "GOOD" : conditionStatus.Trim().ToUpperInvariant();
        Notes = notes;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    private ReceivingLine()
        : base(Guid.NewGuid())
    {
    }

    public Guid ReceivingDocumentId { get; private set; }

    public Guid ItemId { get; private set; }

    public decimal? ExpectedQty { get; private set; }

    public decimal ReceivedQty { get; private set; }

    public decimal RejectedQty { get; private set; }

    public Guid? BatchId { get; private set; }

    public Guid? AssignedLocationId { get; private set; }

    public string ConditionStatus { get; private set; } = "GOOD";

    public bool? ManufacturerPartMatchOk { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class PutawayTask : AuditableEntity
{
    public PutawayTask(Guid id, Guid receivingLineId, Guid fromLocationId, Guid toLocationId, decimal qty, Guid? assignedTo)
        : base(id)
    {
        ReceivingLineId = receivingLineId;
        FromLocationId = fromLocationId;
        ToLocationId = toLocationId;
        Qty = qty;
        Status = "PENDING";
        AssignedTo = assignedTo;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    private PutawayTask()
        : base(Guid.NewGuid())
    {
    }

    public Guid ReceivingLineId { get; private set; }

    public Guid FromLocationId { get; private set; }

    public Guid ToLocationId { get; private set; }

    public decimal Qty { get; private set; }

    public string Status { get; private set; } = "PENDING";

    public Guid? AssignedTo { get; private set; }

    public Guid? ConfirmedBy { get; private set; }

    public DateTimeOffset? ConfirmedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Complete(Guid by)
    {
        Status = "COMPLETED";
        ConfirmedBy = by;
        ConfirmedAt = DateTimeOffset.UtcNow;
        Touch();
    }
}

public sealed class TransferOrder : AuditableEntity
{
    private readonly List<TransferOrderLine> _lines = new();

    public TransferOrder(
        Guid id,
        string transferNo,
        Guid sourceWarehouseId,
        Guid destinationWarehouseId,
        Guid createdBy,
        Guid? transferRequestId = null)
        : base(id)
    {
        TransferNo = transferNo;
        TransferRequestId = transferRequestId;
        SourceWarehouseId = sourceWarehouseId;
        DestinationWarehouseId = destinationWarehouseId;
        Status = "DRAFT";
        CreatedBy = createdBy;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    private TransferOrder()
        : base(Guid.NewGuid())
    {
    }

    public string TransferNo { get; private set; } = string.Empty;

    public Guid? TransferRequestId { get; private set; }

    public Guid SourceWarehouseId { get; private set; }

    public Guid DestinationWarehouseId { get; private set; }

    public string? InternalTrackingNo { get; private set; }

    public string Status { get; private set; } = "DRAFT";

    public DateTimeOffset? ShippedAt { get; private set; }

    public DateTimeOffset? ReceivedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid CreatedBy { get; private set; }

    public IReadOnlyCollection<TransferOrderLine> Lines => _lines.AsReadOnly();

    public Result<TransferOrderLine> AddLine(Guid itemId, Guid? batchId, Guid? sourceLocationId, Guid? destinationLocationId, decimal shippedQty)
    {
        if (Status is not ("DRAFT" or "PICKING"))
        {
            return Result<TransferOrderLine>.Failure(new Error("Transfer.InvalidState", "Transfer order cannot accept lines in current state."));
        }

        var line = new TransferOrderLine(Guid.NewGuid(), Id, itemId, batchId, sourceLocationId, destinationLocationId, shippedQty, 0m);
        _lines.Add(line);
        Touch();
        return Result<TransferOrderLine>.Success(line);
    }

    public Result Ship()
    {
        if (!_lines.Any())
        {
            return Result.Failure(new Error("Transfer.NoLines", "Transfer order must have lines before shipping."));
        }

        Status = "IN_TRANSIT";
        ShippedAt = DateTimeOffset.UtcNow;
        Touch();
        return Result.Success();
    }

    public Result Receive()
    {
        if (Status != "IN_TRANSIT")
        {
            return Result.Failure(new Error("Transfer.InvalidState", "Only in-transit transfer orders can be received."));
        }

        Status = "RECEIVED";
        ReceivedAt = DateTimeOffset.UtcNow;
        Touch();
        return Result.Success();
    }
}

public sealed class TransferOrderLine : AuditableEntity
{
    public TransferOrderLine(
        Guid id,
        Guid transferOrderId,
        Guid itemId,
        Guid? batchId,
        Guid? sourceLocationId,
        Guid? destinationLocationId,
        decimal shippedQty,
        decimal receivedQty)
        : base(id)
    {
        TransferOrderId = transferOrderId;
        ItemId = itemId;
        BatchId = batchId;
        SourceLocationId = sourceLocationId;
        DestinationLocationId = destinationLocationId;
        ShippedQty = shippedQty;
        ReceivedQty = receivedQty;
    }

    private TransferOrderLine()
        : base(Guid.NewGuid())
    {
    }

    public Guid TransferOrderId { get; private set; }

    public Guid ItemId { get; private set; }

    public Guid? BatchId { get; private set; }

    public Guid? SourceLocationId { get; private set; }

    public Guid? DestinationLocationId { get; private set; }

    public decimal ShippedQty { get; private set; }

    public decimal ReceivedQty { get; private set; }
}

public sealed class CycleCountPlan : AuditableEntity
{
    private readonly List<CycleCountLine> _lines = new();

    public CycleCountPlan(Guid id, Guid warehouseId, string scopeType, string? scopeFilterJson, DateOnly scheduledFor, Guid createdBy)
        : base(id)
    {
        WarehouseId = warehouseId;
        ScopeType = scopeType;
        ScopeFilterJson = scopeFilterJson;
        Status = "DRAFT";
        ScheduledFor = scheduledFor;
        CreatedBy = createdBy;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    private CycleCountPlan()
        : base(Guid.NewGuid())
    {
    }

    public Guid WarehouseId { get; private set; }

    public string ScopeType { get; private set; } = string.Empty;

    public string? ScopeFilterJson { get; private set; }

    public string Status { get; private set; } = "DRAFT";

    public DateOnly ScheduledFor { get; private set; }

    public Guid CreatedBy { get; private set; }

    public Guid? PostedBy { get; private set; }

    public DateTimeOffset? PostedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<CycleCountLine> Lines => _lines.AsReadOnly();

    public void AddLine(Guid itemId, Guid locationId, decimal systemQty)
    {
        _lines.Add(new CycleCountLine(Guid.NewGuid(), Id, itemId, locationId, systemQty, null, 0m, null, null));
        Touch();
    }
}

public sealed class CycleCountLine : AuditableEntity
{
    public CycleCountLine(
        Guid id,
        Guid cycleCountPlanId,
        Guid itemId,
        Guid locationId,
        decimal systemQty,
        decimal? countedQty,
        decimal varianceQty,
        string? reasonCode,
        string? notes)
        : base(id)
    {
        CycleCountPlanId = cycleCountPlanId;
        ItemId = itemId;
        LocationId = locationId;
        SystemQty = systemQty;
        CountedQty = countedQty;
        VarianceQty = varianceQty;
        ReasonCode = reasonCode;
        Notes = notes;
    }

    private CycleCountLine()
        : base(Guid.NewGuid())
    {
    }

    public Guid CycleCountPlanId { get; private set; }

    public Guid ItemId { get; private set; }

    public Guid LocationId { get; private set; }

    public decimal SystemQty { get; private set; }

    public decimal? CountedQty { get; private set; }

    public decimal VarianceQty { get; private set; }

    public string? ReasonCode { get; private set; }

    public string? Notes { get; private set; }
}

public sealed class StockAdjustment : AuditableEntity
{
    private readonly List<StockAdjustmentLine> _lines = new();

    public StockAdjustment(Guid id, string adjustmentNo, string adjustmentType, Guid warehouseId, string reasonCode, Guid createdBy)
        : base(id)
    {
        AdjustmentNo = adjustmentNo;
        AdjustmentType = adjustmentType;
        WarehouseId = warehouseId;
        ReasonCode = reasonCode;
        Status = "DRAFT";
        CreatedBy = createdBy;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    private StockAdjustment()
        : base(Guid.NewGuid())
    {
    }

    public string AdjustmentNo { get; private set; } = string.Empty;

    public string AdjustmentType { get; private set; } = string.Empty;

    public Guid WarehouseId { get; private set; }

    public string ReasonCode { get; private set; } = string.Empty;

    public string Status { get; private set; } = "DRAFT";

    public DateTimeOffset? PostedAt { get; private set; }

    public Guid? PostedBy { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyCollection<StockAdjustmentLine> Lines => _lines.AsReadOnly();

    public void AddLine(Guid itemId, Guid locationId, Guid? batchId, string status, decimal qtyDelta, decimal systemQtyBefore, decimal systemQtyAfter, string? notes)
    {
        _lines.Add(new StockAdjustmentLine(
            Guid.NewGuid(),
            Id,
            itemId,
            locationId,
            batchId,
            status,
            qtyDelta,
            systemQtyBefore,
            systemQtyAfter,
            notes));
        Touch();
    }

    public Result Post(Guid by)
    {
        if (!_lines.Any())
        {
            return Result.Failure(new Error("StockAdjustment.NoLines", "Cannot post a stock adjustment without lines."));
        }

        Status = "POSTED";
        PostedAt = DateTimeOffset.UtcNow;
        PostedBy = by;
        Touch();
        return Result.Success();
    }
}

public sealed class StockAdjustmentLine : AuditableEntity
{
    public StockAdjustmentLine(
        Guid id,
        Guid stockAdjustmentId,
        Guid itemId,
        Guid locationId,
        Guid? batchId,
        string status,
        decimal qtyDelta,
        decimal systemQtyBefore,
        decimal systemQtyAfter,
        string? notes)
        : base(id)
    {
        StockAdjustmentId = stockAdjustmentId;
        ItemId = itemId;
        LocationId = locationId;
        BatchId = batchId;
        Status = status;
        QtyDelta = qtyDelta;
        SystemQtyBefore = systemQtyBefore;
        SystemQtyAfter = systemQtyAfter;
        Notes = notes;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    private StockAdjustmentLine()
        : base(Guid.NewGuid())
    {
    }

    public Guid StockAdjustmentId { get; private set; }

    public Guid ItemId { get; private set; }

    public Guid LocationId { get; private set; }

    public Guid? BatchId { get; private set; }

    public string Status { get; private set; } = string.Empty;

    public decimal QtyDelta { get; private set; }

    public decimal SystemQtyBefore { get; private set; }

    public decimal SystemQtyAfter { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class BarcodeScanLog : AuditableEntity
{
    public BarcodeScanLog(Guid id, string scanCode, string scanType, Guid? itemId, Guid? batchId, Guid? locationId, Guid scannedBy)
        : base(id)
    {
        ScanCode = scanCode;
        ScanType = scanType;
        ItemId = itemId;
        BatchId = batchId;
        LocationId = locationId;
        ScannedBy = scannedBy;
        ScannedAt = DateTimeOffset.UtcNow;
    }

    private BarcodeScanLog()
        : base(Guid.NewGuid())
    {
    }

    public string ScanCode { get; private set; } = string.Empty;

    public string ScanType { get; private set; } = "UNKNOWN";

    public Guid? ItemId { get; private set; }

    public Guid? BatchId { get; private set; }

    public Guid? LocationId { get; private set; }

    public Guid ScannedBy { get; private set; }

    public DateTimeOffset ScannedAt { get; private set; }

    public string? DeviceId { get; private set; }
}
