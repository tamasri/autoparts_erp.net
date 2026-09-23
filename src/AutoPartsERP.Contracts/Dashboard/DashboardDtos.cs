namespace AutoPartsERP.Contracts.Dashboard;

/// <summary>Today's work on the home screen. The business figures (sales, profit, receivables, stock) are in <see cref="BusinessKpisDto"/>.</summary>
public sealed record DashboardSummaryDto(
    int OpenAlerts,
    IReadOnlyCollection<DashboardRecentInvoiceDto> RecentInvoices);

public sealed record DashboardRecentInvoiceDto(
    Guid Id,
    string InvoiceNumber,
    string CustomerName,
    DateOnly InvoiceDate,
    decimal TotalSyp,
    decimal TotalUsd,
    string Status);
