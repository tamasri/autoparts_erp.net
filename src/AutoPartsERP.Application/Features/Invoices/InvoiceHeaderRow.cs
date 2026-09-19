namespace AutoPartsERP.Application.Features.Invoices;

/// <summary>
/// Typed projection of an invoice header. Do not read these rows as <c>dynamic</c>: Dapper hands date columns back as
/// DateTime and lower-cases aliases, so binding to DateOnly / PascalCase members fails at runtime.
/// </summary>
internal sealed record InvoiceHeaderRow(
    Guid Id, string? InvoiceNumber, string Status, string Type, Guid CustomerId, string CustomerCode, string CustomerName,
    DateOnly InvoiceDate, DateOnly DueDate, decimal TotalSyp, decimal TotalUsd, decimal PaidSyp, decimal PaidUsd);
