namespace AutoPartsERP.Contracts.Dashboard;

/// <summary>
/// The business figures for a period, all in dollars and all aggregated on the server. Each section says what it is built from so it can be
/// matched with its detailed report. A section is null when the user may not see it or when a filter does not apply to it.
/// </summary>
public sealed record BusinessKpisDto(
    DateOnly From,
    DateOnly To,
    KpiSalesDto Sales,
    KpiLedgerDto? Ledger,
    string? LedgerNote,
    KpiPurchasesDto? Purchases,
    KpiAgeingDto Receivables,
    KpiAgeingDto? Payables,
    KpiCashDto? Cash,
    KpiStockDto? Stock,
    IReadOnlyList<KpiMonthDto> Months,
    IReadOnlyList<KpiRankDto> TopCustomers,
    IReadOnlyList<KpiRankDto> TopItems,
    IReadOnlyList<KpiRankDto> SalesByRep,
    IReadOnlyList<KpiSlowMoverDto> SlowMovers,
    IReadOnlyList<KpiOverdueDto> Overdue);

/// <summary>
/// Posted sales and returns, by invoice line scaled to its invoice net total (after line and invoice discounts, without delivery fees and tax), so they add up to the invoices.
/// Cost is the cost recorded on the line when it was sold. Returns count negative in net sales and cost.
/// </summary>
public sealed record KpiSalesDto(
    decimal GrossSalesUsd, decimal ReturnsUsd, decimal NetSalesUsd, decimal CostUsd, decimal GrossProfitUsd, decimal? GrossMarginPct,
    int InvoiceCount, decimal? AverageInvoiceUsd, int CustomerCount);

/// <summary>From ERPNext's profit and loss for the same period (company-wide; not available with other filters).</summary>
public sealed record KpiLedgerDto(decimal IncomeUsd, decimal ExpensesUsd, decimal NetProfitUsd);

/// <summary>Posted supplier bills in the period, by line after line and bill discounts.</summary>
public sealed record KpiPurchasesDto(decimal TotalUsd, int BillCount);

/// <summary>What is still owed on posted invoices dated up to the end of the period, by days past due at that date. DSO = owed ÷ net sales × days.</summary>
public sealed record KpiAgeingDto(
    decimal TotalUsd, decimal NotDueUsd, decimal Days1To30Usd, decimal Days31To60Usd, decimal Days61To90Usd, decimal Over90Usd,
    int OverdueCount, decimal? DaysSalesOutstanding);

/// <summary>Receipts and supplier payments dated in the period (reversed ones excluded).</summary>
public sealed record KpiCashDto(decimal ReceiptsUsd, decimal? SupplierPaymentsUsd, decimal? NetUsd);

/// <summary>Stock on hand today at item cost. Slow movers: in stock with no sale in the 90 days before the end of the period.</summary>
public sealed record KpiStockDto(decimal ValueUsd, int SkusInStock, int SkusOutOfStock, int SkusBelowReorder, int SlowMoverCount, decimal SlowMoverValueUsd);

public sealed record KpiMonthDto(int Year, int Month, decimal NetSalesUsd, decimal GrossProfitUsd, decimal PurchasesUsd, decimal ReceiptsUsd);

/// <summary>A ranked row: <c>Secondary</c> is gross profit (customers, reps) or quantity (items).</summary>
public sealed record KpiRankDto(Guid? Id, string Name, decimal AmountUsd, decimal Secondary, int Count);

public sealed record KpiSlowMoverDto(Guid SkuId, string Code, string Name, decimal Quantity, decimal ValueUsd, DateOnly? LastSale);

public sealed record KpiOverdueDto(Guid InvoiceId, string? InvoiceNumber, string CustomerName, DateOnly DueDate, int DaysOverdue, decimal BalanceUsd);
