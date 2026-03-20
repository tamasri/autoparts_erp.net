namespace AutoPartsERP.Contracts.Payments;

public sealed record CreatePaymentRequest(
    string PaymentType,
    Guid CustomerId,
    DateOnly PaymentDate,
    string PaymentMethod,
    decimal AmountSyp,
    decimal AmountUsd,
    Guid FxRateId,
    string? ReferenceNumber,
    string? BankName,
    string? ChequeNumber,
    DateOnly? ChequeDate,
    string? Notes);

public sealed record AllocatePaymentLineRequest(
    Guid InvoiceId,
    decimal AllocatedSyp,
    decimal AllocatedUsd);

public sealed record AllocatePaymentRequest(
    IReadOnlyCollection<AllocatePaymentLineRequest> Allocations,
    string? Notes);

public sealed record ReversePaymentRequest(
    string Reason);

public sealed record PaymentQueryRequest(
    int PageNumber = 1,
    int PageSize = 20,
    Guid? CustomerId = null,
    string? PaymentMethod = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null);
