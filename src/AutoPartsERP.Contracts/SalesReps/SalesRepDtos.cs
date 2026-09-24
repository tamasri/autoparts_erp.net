namespace AutoPartsERP.Contracts.SalesReps;

/// <summary>
/// A sales representative and how they did in the chosen period (posted invoices only, in dollars).
/// Net sales = sales − returns. Gross profit = revenue after line and invoice discounts, without delivery fees and tax, minus the cost
/// of the goods (returns subtract theirs); the commission is taken on it. Target = monthly target × months in the period, set in net
/// sales; achievement = net sales ÷ target. Collected = receipts allocated to the rep's invoices in the period. Outstanding = what the
/// rep's customers still owe on their invoices today. Percentages are null when there is nothing to divide by.
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
    decimal GrossProfitUsd,
    decimal? GrossMarginPct,
    decimal CollectedUsd,
    decimal OutstandingUsd,
    decimal CommissionUsd,
    decimal TargetUsd,
    decimal? AchievementPct,
    decimal AverageInvoiceUsd,
    decimal? ReturnRatePct,
    decimal? CollectionRatePct);

public sealed record SalesRepCandidateDto(Guid UserId, string UserName, string FullName);

public sealed record SaveSalesRepRequest(decimal CommissionPct, decimal MonthlyTargetUsd, bool IsActive, string? Notes);

/// <summary>Customers to give to this rep (they leave their previous rep).</summary>
public sealed record AssignSalesRepCustomersRequest(IReadOnlyList<Guid> CustomerIds);

public sealed record SalesRepCustomerDto(Guid Id, string Code, string Name, string? Phone, decimal OutstandingUsd, DateOnly? LastInvoiceDate);

public sealed record SalesRepInvoiceDto(
    Guid Id, string? InvoiceNumber, string Type, DateOnly InvoiceDate, string CustomerName, decimal TotalUsd, decimal BalanceUsd, decimal GrossProfitUsd);

/// <summary>One month of the rep's trend: the target, what was achieved against it, the gross profit and the commission it earned.</summary>
public sealed record SalesRepMonthDto(
    int Year, int Month, decimal TargetUsd, decimal NetSalesUsd, decimal GrossProfitUsd, decimal CommissionUsd, decimal CollectedUsd);

public sealed record SalesRepDetailDto(
    SalesRepDto Rep,
    IReadOnlyList<SalesRepMonthDto> Months,
    IReadOnlyList<SalesRepCustomerDto> Customers,
    IReadOnlyList<SalesRepInvoiceDto> Invoices);
