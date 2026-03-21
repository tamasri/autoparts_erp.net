namespace AutoPartsERP.Domain.Operational;

public sealed class PaymentAllocation : AuditableEntity
{
    private PaymentAllocation() : base(Guid.Empty) { }

    public PaymentAllocation(
        Guid id,
        Guid paymentId,
        Guid invoiceId,
        decimal allocatedSyp,
        decimal allocatedUsd,
        DateOnly allocationDate,
        Guid createdBy)
        : base(id)
    {
        PaymentId = paymentId;
        InvoiceId = invoiceId;
        AllocatedSyp = allocatedSyp;
        AllocatedUsd = allocatedUsd;
        AllocationDate = allocationDate;
        CreatedBy = createdBy;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid PaymentId { get; private set; }

    public Guid InvoiceId { get; private set; }

    public decimal AllocatedSyp { get; private set; }

    public decimal AllocatedUsd { get; private set; }

    public DateOnly AllocationDate { get; private set; }

    public string? Notes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid CreatedBy { get; private set; }
}
