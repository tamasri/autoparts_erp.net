namespace AutoPartsERP.Application.Features.Invoices;

/// <summary>
/// Typed projection of an invoice header. Do not read these rows as <c>dynamic</c>: Dapper hands date columns back as
/// DateTime and lower-cases aliases, so binding to DateOnly / PascalCase members fails at runtime.
/// </summary>
internal sealed record InvoiceHeaderRow(
    Guid Id, string? InvoiceNumber, string Status, string Type, Guid CustomerId, string CustomerCode, string CustomerName,
    DateOnly InvoiceDate, DateOnly DueDate, decimal TotalSyp, decimal TotalUsd, decimal PaidSyp, decimal PaidUsd,
    decimal SubtotalSyp, decimal SubtotalUsd, decimal? DiscountPct, decimal DiscountAmountSyp, decimal DiscountAmountUsd,
    decimal DeliveryFeeSyp, decimal DeliveryFeeUsd, decimal BalanceSyp, decimal BalanceUsd, decimal CreditAppliedSyp, decimal CreditAppliedUsd,
    Guid? OriginalInvoiceId);

/// <summary>Reads one invoice with its lines — the single loader behind "get invoice" and the answer to "create invoice".</summary>
internal static class InvoiceReader
{
    public static async Task<InvoiceDto?> LoadAsync(DbConnection connection, DbTransaction? transaction, Guid invoiceId, CancellationToken cancellationToken)
    {
        var header = await connection.QuerySingleOrDefaultAsync<InvoiceHeaderRow>(new CommandDefinition(
            """
            SELECT i.id AS Id, i.invoice_number AS InvoiceNumber, i.status AS Status, i.invoice_type AS Type, i.customer_id AS CustomerId,
                   c.code AS CustomerCode, c.name AS CustomerName, i.invoice_date AS InvoiceDate, i.due_date AS DueDate,
                   i.total_syp AS TotalSyp, i.total_usd AS TotalUsd, i.paid_syp AS PaidSyp, i.paid_usd AS PaidUsd,
                   i.subtotal_syp AS SubtotalSyp, i.subtotal_usd AS SubtotalUsd, i.discount_pct AS DiscountPct,
                   i.discount_amount_syp AS DiscountAmountSyp, i.discount_amount_usd AS DiscountAmountUsd,
                   i.delivery_fee_syp AS DeliveryFeeSyp, i.delivery_fee_usd AS DeliveryFeeUsd, i.balance_syp AS BalanceSyp, i.balance_usd AS BalanceUsd,
                   i.credit_applied_syp AS CreditAppliedSyp, i.credit_applied_usd AS CreditAppliedUsd, i.original_invoice_id AS OriginalInvoiceId
            FROM invoices i
            INNER JOIN customers c ON c.id = i.customer_id
            WHERE i.id = @invoiceId;
            """,
            new { invoiceId }, transaction, cancellationToken: cancellationToken));
        if (header is null)
        {
            return null;
        }

        var lines = (await connection.QueryAsync<InvoiceLineDto>(new CommandDefinition(
            """
            SELECT il.id AS Id, il.line_number AS LineNumber, il.sku_id AS SkuId, s.code AS SkuCode, s.name AS SkuName,
                   il.batch_id AS BatchId, il.location_id AS LocationId, il.quantity AS Quantity,
                   il.unit_price_syp AS UnitPriceSyp, il.unit_price_usd AS UnitPriceUsd, il.discount_pct AS DiscountPct,
                   il.line_total_syp AS LineTotalSyp, il.line_total_usd AS LineTotalUsd,
                   il.quantity * il.cost_price_syp AS GrossMarginSyp, il.quantity * il.cost_price_usd AS GrossMarginUsd,
                   CASE WHEN il.line_total_syp = 0 THEN 0 ELSE ((il.line_total_syp - (il.quantity * il.cost_price_syp)) / il.line_total_syp) * 100 END AS GrossMarginPct,
                   il.is_price_override AS IsPriceOverride, il.price_override_reason AS OverrideReason, il.return_of_line_id AS ReturnOfLineId
            FROM invoice_lines il
            INNER JOIN skus s ON s.id = il.sku_id
            WHERE il.invoice_id = @invoiceId
            ORDER BY il.line_number;
            """,
            new { invoiceId }, transaction, cancellationToken: cancellationToken))).ToArray();

        // The sale a return gives back to, or the returns made against a sale.
        var links = (await connection.QueryAsync<DocumentLinkDto>(new CommandDefinition(
            """
            SELECT id AS Id, invoice_number AS Number, status AS Status, invoice_date AS Date, total_usd AS TotalUsd
            FROM invoices
            WHERE (id = @OriginalInvoiceId AND @Type = 'RETURN') OR (original_invoice_id = @invoiceId AND invoice_type = 'RETURN')
            ORDER BY serial_no;
            """,
            new { invoiceId, header.OriginalInvoiceId, header.Type }, transaction, cancellationToken: cancellationToken))).ToList();

        return InvoiceMappings.ToInvoiceDto(header, lines, links.FirstOrDefault(l => l.Id == header.OriginalInvoiceId), links.Where(l => l.Id != header.OriginalInvoiceId).ToList());
    }
}
