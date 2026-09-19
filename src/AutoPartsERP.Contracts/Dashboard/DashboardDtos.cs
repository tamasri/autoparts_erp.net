namespace AutoPartsERP.Contracts.Dashboard;

public sealed record DashboardSummaryDto(
    int ActiveCustomers,
    int PostedInvoices,
    decimal ReceivablesSyp,
    decimal ReceivablesUsd,
    int OverdueInvoices,
    decimal OverdueSyp,
    decimal SalesTodaySyp,
    decimal SalesTodayUsd,
    decimal SalesMonthSyp,
    decimal SalesMonthUsd,
    int SkusInStock,
    int SkusOutOfStock,
    int SkusLowStock,
    int OpenAlerts,
    IReadOnlyCollection<DashboardSalesDayDto> SalesByDay,
    IReadOnlyCollection<DashboardTopCustomerDto> TopCustomers,
    IReadOnlyCollection<DashboardRecentInvoiceDto> RecentInvoices);

public sealed record DashboardSalesDayDto(
    DateOnly Day,
    decimal TotalSyp,
    decimal TotalUsd,
    int InvoiceCount);

public sealed record DashboardTopCustomerDto(
    Guid CustomerId,
    string CustomerName,
    decimal TotalSyp,
    decimal TotalUsd,
    int InvoiceCount);

public sealed record DashboardRecentInvoiceDto(
    Guid Id,
    string InvoiceNumber,
    string CustomerName,
    DateOnly InvoiceDate,
    decimal TotalSyp,
    decimal TotalUsd,
    string Status);
