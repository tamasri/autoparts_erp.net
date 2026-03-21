namespace AutoPartsERP.Domain.Wms;

public static class InventoryStatusCodes
{
    public const string Available = "AVAILABLE";
    public const string QcHold = "QC_HOLD";
    public const string InTransit = "IN_TRANSIT";
    public const string StopShip = "STOP_SHIP";
    public const string Receiving = "RECEIVING";
    public const string Reserved = "RESERVED";

    public static readonly IReadOnlyCollection<string> All = new[]
    {
        Available,
        QcHold,
        InTransit,
        StopShip,
        Receiving,
        Reserved
    };
}

public sealed class InventoryBalance : AuditableEntity
{
    public InventoryBalance(Guid id, Guid itemId, Guid locationId, Guid? batchId, string status, decimal qty)
        : base(id)
    {
        ItemId = itemId;
        LocationId = locationId;
        BatchId = batchId;
        Status = status;
        Qty = qty;
    }

    private InventoryBalance()
        : base(Guid.NewGuid())
    {
    }

    public Guid ItemId { get; private set; }

    public Guid LocationId { get; private set; }

    public Guid? BatchId { get; private set; }

    public string Status { get; private set; } = InventoryStatusCodes.Available;

    public decimal Qty { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public Result AddQty(decimal qty)
    {
        if (qty <= 0)
        {
            return Result.Failure(new Error("Stock.InvalidQuantity", "Quantity to add must be greater than zero."));
        }

        Qty += qty;
        UpdatedAt = DateTimeOffset.UtcNow;
        Touch();
        return Result.Success();
    }

    public Result DeductQty(decimal qty)
    {
        if (qty <= 0)
        {
            return Result.Failure(new Error("Stock.InvalidQuantity", "Quantity to deduct must be greater than zero."));
        }

        if (qty > Qty)
        {
            return Result.Failure(new Error("Stock.InsufficientQuantity", "Insufficient stock quantity."));
        }

        Qty -= qty;
        UpdatedAt = DateTimeOffset.UtcNow;
        Touch();
        return Result.Success();
    }

    public Result MoveToStatus(string toStatus, decimal qty)
    {
        if (!InventoryStatusCodes.All.Contains(toStatus))
        {
            return Result.Failure(new Error("Stock.InvalidStatus", "Inventory status is invalid."));
        }

        return DeductQty(qty);
    }
}

public sealed class InventoryMovement : AuditableEntity
{
    public InventoryMovement(
        Guid id,
        Guid itemId,
        Guid locationId,
        Guid? batchId,
        string movementType,
        decimal qty,
        string direction,
        string? fromStatus,
        string? toStatus,
        string? referenceType,
        Guid? referenceId,
        Guid performedBy,
        Guid correlationId,
        string? notes)
        : base(id)
    {
        ItemId = itemId;
        LocationId = locationId;
        BatchId = batchId;
        MovementType = movementType;
        Qty = qty;
        Direction = direction;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ReferenceType = referenceType;
        ReferenceId = referenceId;
        PerformedBy = performedBy;
        CorrelationId = correlationId;
        Notes = notes;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    private InventoryMovement()
        : base(Guid.NewGuid())
    {
    }

    public Guid ItemId { get; private set; }

    public Guid LocationId { get; private set; }

    public Guid? BatchId { get; private set; }

    public string MovementType { get; private set; } = string.Empty;

    public decimal Qty { get; private set; }

    public string Direction { get; private set; } = "IN";

    public string? FromStatus { get; private set; }

    public string? ToStatus { get; private set; }

    public string? ReferenceType { get; private set; }

    public Guid? ReferenceId { get; private set; }

    public Guid PerformedBy { get; private set; }

    public Guid CorrelationId { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class InventoryAlert : AuditableEntity
{
    public InventoryAlert(Guid id, Guid itemId, string alertType, string severity, string message, decimal? thresholdValue, decimal? currentValue)
        : base(id)
    {
        ItemId = itemId;
        AlertType = alertType;
        Severity = severity;
        Message = message;
        ThresholdValue = thresholdValue;
        CurrentValue = currentValue;
        Status = "OPEN";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    private InventoryAlert()
        : base(Guid.NewGuid())
    {
    }

    public Guid ItemId { get; private set; }

    public string AlertType { get; private set; } = string.Empty;

    public string Severity { get; private set; } = "INFO";

    public string Message { get; private set; } = string.Empty;

    public decimal? ThresholdValue { get; private set; }

    public decimal? CurrentValue { get; private set; }

    public string Status { get; private set; } = "OPEN";

    public Guid? AcknowledgedBy { get; private set; }

    public DateTimeOffset? AcknowledgedAt { get; private set; }

    public Guid? ResolvedBy { get; private set; }

    public DateTimeOffset? ResolvedAt { get; private set; }

    public string? ResolutionNote { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Acknowledge(Guid by)
    {
        Status = "ACKNOWLEDGED";
        AcknowledgedBy = by;
        AcknowledgedAt = DateTimeOffset.UtcNow;
        Touch();
    }

    public void Resolve(Guid by, string? note)
    {
        Status = "RESOLVED";
        ResolvedBy = by;
        ResolvedAt = DateTimeOffset.UtcNow;
        ResolutionNote = note;
        Touch();
    }
}

