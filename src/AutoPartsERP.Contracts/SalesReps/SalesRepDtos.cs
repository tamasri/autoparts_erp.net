namespace AutoPartsERP.Contracts.SalesReps;

/// <summary>
/// A sales representative and how they did in the chosen period (posted invoices only, in dollars).
/// Net sales = sales − returns. Commission is taken on net sales without delivery fees and tax. Target = monthly target × months in the period.
/// Collected = receipts allocated to the rep's invoices in the period. Outstanding = what the rep's customers still owe on their invoices today.
/// </summary>
public sealed record SalesRepDto(
    Guid UserId,
    string UserName,
    string FullName,
    string? Phone,
    decimal CommissionPct,
    decimal MonthlyTargetUsd,
    bool IsActive,
    string? Notes,
    int CustomerCount,
    int InvoiceCount,
    decimal SalesUsd,
    decimal ReturnsUsd,
    decimal NetSalesUsd,
    decimal CollectedUsd,
    decimal OutstandingUsd,
    decimal CommissionUsd,
    decimal TargetUsd);

public sealed record SalesRepCandidateDto(Guid UserId, string UserName, string FullName);

public sealed record SaveSalesRepRequest(decimal CommissionPct, decimal MonthlyTargetUsd, bool IsActive, string? Notes);

/// <summary>Customers to give to this rep (they leave their previous rep).</summary>
public sealed record AssignSalesRepCustomersRequest(IReadOnlyList<Guid> CustomerIds);

public sealed record SalesRepCustomerDto(Guid Id, string Code, string Name, string? Phone, decimal OutstandingUsd, DateOnly? LastInvoiceDate);

public sealed record SalesRepInvoiceDto(Guid Id, string? InvoiceNumber, string Type, DateOnly InvoiceDate, string CustomerName, decimal TotalUsd, decimal BalanceUsd);

public sealed record SalesRepMonthDto(int Year, int Month, decimal NetSalesUsd, decimal CollectedUsd);

public sealed record SalesRepDetailDto(
    SalesRepDto Rep,
    IReadOnlyList<SalesRepMonthDto> Months,
    IReadOnlyList<SalesRepCustomerDto> Customers,
    IReadOnlyList<SalesRepInvoiceDto> Invoices);
