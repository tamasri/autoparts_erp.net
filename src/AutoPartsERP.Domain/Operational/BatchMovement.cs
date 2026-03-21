using AutoPartsERP.Domain.Operational.Enums;

namespace AutoPartsERP.Domain.Operational;

public sealed class BatchMovement : AuditableEntity
{
    private BatchMovement() : base(Guid.Empty) { }

    public BatchMovement(
        Guid id,
        Guid batchId,
        MovementType movementType,
        decimal quantity,
        string direction,
        Guid performedBy)
        : base(id)
    {
        BatchId = batchId;
        MovementType = movementType;
        Quantity = quantity;
        Direction = direction;
        PerformedBy = performedBy;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid BatchId { get; private set; }

    public MovementType MovementType { get; private set; }

    public decimal Quantity { get; private set; }

    public string Direction { get; private set; } = "IN";

    public string? ReferenceType { get; private set; }

    public Guid? ReferenceId { get; private set; }

    public Guid? FromLocationId { get; private set; }

    public Guid? ToLocationId { get; private set; }

    public decimal UnitCostSyp { get; private set; }

    public decimal UnitCostUsd { get; private set; }

    public Guid PerformedBy { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
