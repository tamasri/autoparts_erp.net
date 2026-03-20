namespace AutoPartsERP.Contracts.Customers;

public sealed record CustomerDto(
    Guid Id,
    string Code,
    string Name,
    string Type,
    string? Phone,
    string? Address,
    decimal CreditLimitSyp,
    decimal CreditLimitUsd,
    int PaymentTermsDays,
    bool IsActive,
    Guid? AssignedSalesRep,
    string StatusDisplay,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public sealed record CustomerStatementTransactionDto(
    Guid Id,
    string Type,
    DateTime Date,
    DateTime? DueDate,
    decimal DebitSyp,
    decimal CreditSyp,
    decimal DebitUsd,
    decimal CreditUsd,
    decimal BalanceSyp,
    decimal BalanceUsd,
    string DueDateDisplay);

public sealed record CustomerAccountStatementDto(
    Guid CustomerId,
    string CustomerCode,
    string CustomerName,
    decimal TotalInvoicedSyp,
    decimal TotalInvoicedUsd,
    decimal TotalPaidSyp,
    decimal TotalPaidUsd,
    decimal OutstandingSyp,
    decimal OutstandingUsd,
    IReadOnlyCollection<CustomerStatementTransactionDto> Transactions);
