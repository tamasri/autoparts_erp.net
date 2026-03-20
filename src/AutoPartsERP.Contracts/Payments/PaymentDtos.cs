namespace AutoPartsERP.Contracts.Payments;

public sealed record PaymentAllocationDto(
    Guid PaymentId,
    Guid InvoiceId,
    decimal AllocatedSyp,
    decimal AllocatedUsd,
    DateOnly AllocationDate);

public sealed record PaymentDto(
    Guid Id,
    string PaymentNumber,
    string PaymentType,
    Guid CustomerId,
    string CustomerName,
    DateOnly PaymentDate,
    string PaymentMethod,
    decimal AmountSyp,
    decimal AmountUsd,
    decimal AllocatedSyp,
    decimal AllocatedUsd,
    decimal UnallocatedSyp,
    decimal UnallocatedUsd,
    bool IsReversed,
    string PaymentMethodDisplay,
    string ReceivedDisplay);
