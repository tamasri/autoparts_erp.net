using AutoPartsERP.Domain.Operational.Enums;

namespace AutoPartsERP.Domain.Operational;

public sealed class Payment : AuditableEntity
{
    private Payment(
        Guid id,
        Guid customerId,
        DateOnly paymentDate,
        PaymentType paymentType,
        PaymentMethod paymentMethod,
        decimal amountSyp,
        decimal amountUsd,
        Guid fxRateId,
        Guid receivedBy,
        Guid createdBy)
        : base(id)
    {
        CustomerId = customerId;
        PaymentDate = paymentDate;
        PaymentType = paymentType;
        PaymentMethod = paymentMethod;
        AmountSyp = amountSyp;
        AmountUsd = amountUsd;
        FxRateId = fxRateId;
        ReceivedBy = receivedBy;
        CreatedBy = createdBy;
    }

    public string? PaymentNumber { get; private set; }

    public PaymentType PaymentType { get; private set; }

    public Guid CustomerId { get; private set; }

    public DateOnly PaymentDate { get; private set; }

    public PaymentMethod PaymentMethod { get; private set; }

    public decimal AmountSyp { get; private set; }

    public decimal AmountUsd { get; private set; }

    public decimal AllocatedSyp { get; private set; }

    public decimal AllocatedUsd { get; private set; }

    public decimal UnallocatedSyp => AmountSyp - AllocatedSyp;

    public decimal UnallocatedUsd => AmountUsd - AllocatedUsd;

    public Guid FxRateId { get; private set; }

    public bool IsReversed { get; private set; }

    public DateTimeOffset? ReversedAt { get; private set; }

    public Guid? ReversedBy { get; private set; }

    public string? ReversalReason { get; private set; }

    public Guid ReceivedBy { get; private set; }

    public Guid CreatedBy { get; private set; }

    public static Result<Payment> Create(
        Guid customerId,
        DateOnly paymentDate,
        PaymentType paymentType,
        PaymentMethod paymentMethod,
        decimal amountSyp,
        decimal amountUsd,
        Guid fxRateId,
        Guid receivedBy,
        Guid createdBy)
    {
        if (amountSyp < 0 || amountUsd < 0 || (amountSyp == 0m && amountUsd == 0m))
        {
            return Result<Payment>.Failure(new Error("Payment.InvalidAmount", "Payment amount must be greater than zero in at least one currency."));
        }

        return Result<Payment>.Success(new Payment(
            Guid.NewGuid(),
            customerId,
            paymentDate,
            paymentType,
            paymentMethod,
            amountSyp,
            amountUsd,
            fxRateId,
            receivedBy,
            createdBy));
    }

    public Result Allocate(decimal syp, decimal usd)
    {
        if (syp < 0 || usd < 0 || (syp == 0m && usd == 0m))
        {
            return Result.Failure(new Error("Payment.InvalidAllocation", "Allocation must be greater than zero."));
        }

        if (syp > UnallocatedSyp || usd > UnallocatedUsd)
        {
            return Result.Failure(new Error("Payment.OverAllocation", "Allocation exceeds unallocated payment amount."));
        }

        AllocatedSyp += syp;
        AllocatedUsd += usd;
        Touch();
        return Result.Success();
    }

    public Result Reverse(string reason, Guid by)
    {
        if (IsReversed)
        {
            return Result.Failure(new Error("Payment.AlreadyReversed", "Payment is already reversed."));
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(new Error("Payment.ReversalReasonRequired", "Reversal reason is required."));
        }

        IsReversed = true;
        ReversalReason = reason.Trim();
        ReversedAt = DateTimeOffset.UtcNow;
        ReversedBy = by;
        Touch();
        return Result.Success();
    }
}
