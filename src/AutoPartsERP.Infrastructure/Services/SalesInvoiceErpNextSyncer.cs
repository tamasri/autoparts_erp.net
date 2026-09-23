namespace AutoPartsERP.Infrastructure.Services;

/// <summary>
/// Pushes a posted sales invoice (or customer return) to ERPNext, books its cost of goods sold, and cancels both when the
/// invoice is voided. It first makes sure the customer and the items exist in ERPNext (idempotent upserts), so a brand-new
/// customer or item no longer makes the invoice fail until the next scheduled catalog sync. Names, not ids: customer = party
/// display name, item_code = sku code. Every outcome is recorded in erpnext_sync_log.
/// </summary>
public sealed class SalesInvoiceErpNextSyncer
{
    private const string Entity = "Invoice";
    private const string Doctype = "Sales Invoice";
    private const string CogsDoctype = "Journal Entry";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IErpNextClient _erpNextClient;

    public SalesInvoiceErpNextSyncer(IDbConnectionFactory connectionFactory, IErpNextClient erpNextClient)
    {
        _connectionFactory = connectionFactory;
        _erpNextClient = erpNextClient;
    }

    public async Task SyncAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);

        var header = await connection.QuerySingleOrDefaultAsync<InvoiceHeader>(new CommandDefinition(
            """
            SELECT i.invoice_number AS InvoiceNumber, i.invoice_type AS Type, i.original_invoice_id AS OriginalInvoiceId,
                   p.id AS PartyId, p.display_name AS CustomerName, p.tax_number AS TaxNumber,
                   i.invoice_date AS InvoiceDate, i.due_date AS DueDate, i.discount_amount_usd AS DiscountAmountUsd, i.delivery_fee_usd AS DeliveryFeeUsd,
                   r.user_id AS SalesRepId, COALESCE(NULLIF(u.full_name, ''), u.user_name) AS SalesRepName,
                   COALESCE(r.commission_pct, 0) AS SalesRepCommission, COALESCE(r.is_active, FALSE) AS SalesRepActive
            FROM invoices i
            INNER JOIN customers c ON c.id = i.customer_id
            INNER JOIN parties p ON p.id = c.party_id
            LEFT JOIN sales_reps r ON r.user_id = i.sales_rep_id
            LEFT JOIN asp_net_users u ON u.id = r.user_id
            WHERE i.id = @invoiceId AND i.status = 'POSTED' AND i.invoice_type IN ('SALE', 'RETURN');
            """,
            new { invoiceId },
            cancellationToken: cancellationToken));
        if (header is null)
        {
            return;
        }

        var lines = (await connection.QueryAsync<InvoiceLineRow>(new CommandDefinition(
            """
            SELECT s.id AS SkuId, s.code AS ItemCode, s.name AS NameEn, s.name_ar AS NameAr,
                   s.cost_price_usd AS CostPrice, s.selling_price_usd AS SellingPrice,
                   l.quantity AS Quantity, l.unit_price_usd AS UnitPrice, l.discount_pct AS DiscountPercent,
                   l.cost_price_usd AS LineCostUsd
            FROM invoice_lines l
            INNER JOIN skus s ON s.id = l.sku_id
            WHERE l.invoice_id = @invoiceId
            ORDER BY l.line_number;
            """,
            new { invoiceId },
            cancellationToken: cancellationToken))).ToList();

        var isReturn = string.Equals(header.Type, "RETURN", StringComparison.OrdinalIgnoreCase);

        var invoiceName = await ErpNextSyncLogWriter.FindSyncedNameAsync(connection, Entity, invoiceId, Doctype, cancellationToken);
        if (invoiceName is null)
        {
            // Master data first: ERPNext rejects an invoice whose customer or items it has never seen.
            var prerequisite = await EnsureMasterDataAsync(connection, header, lines, cancellationToken);
            if (prerequisite.IsFailure)
            {
                await ErpNextSyncLogWriter.WriteAsync(connection, Entity, invoiceId, Doctype, null,
                    _erpNextClient.IsEnabled ? ErpNextSyncLogWriter.Failed : ErpNextSyncLogWriter.Skipped, prerequisite.Error.Message, cancellationToken);
                return;
            }

            var customerName = prerequisite.Value!; // the ERPNext record of this customer, never the display name

            string? returnAgainst = null;
            if (isReturn && header.OriginalInvoiceId is { } original)
            {
                returnAgainst = await ErpNextSyncLogWriter.FindSyncedNameAsync(connection, Entity, original, Doctype, cancellationToken);
            }

            var result = await _erpNextClient.SyncSalesInvoiceAsync(
                new ErpNextSalesInvoiceSync(
                    invoiceId,
                    header.InvoiceNumber ?? invoiceId.ToString(),
                    customerName,
                    header.InvoiceDate,
                    header.DueDate,
                    lines.Select(l => new ErpNextInvoiceLineSync(l.ItemCode, l.Quantity, l.UnitPrice, l.DiscountPercent)).ToList(),
                    isReturn,
                    returnAgainst,
                    header.DiscountAmountUsd,
                    header.SalesRepId is null ? null : header.SalesRepName,
                    header.DeliveryFeeUsd),
                cancellationToken);

            await ErpNextSyncLogWriter.WriteAsync(
                connection, Entity, invoiceId, Doctype,
                result.IsSuccess ? result.Value : null,
                _erpNextClient.IsEnabled ? (result.IsSuccess ? ErpNextSyncLogWriter.Synced : ErpNextSyncLogWriter.Failed) : ErpNextSyncLogWriter.Skipped,
                result.IsFailure ? result.Error.Message : null,
                cancellationToken);

            if (result.IsFailure)
            {
                return;
            }
        }

        await EnsureCogsAsync(connection, invoiceId, header, lines, isReturn, cancellationToken);
    }

    /// <summary>
    /// The Sales Invoice books revenue and the receivable only (this application owns stock, so ERPNext has no stock ledger).
    /// A Journal Entry books the matching cost of goods sold against the inventory account, so ERPNext's profit is not overstated.
    /// </summary>
    private async Task EnsureCogsAsync(DbConnection connection, Guid invoiceId, InvoiceHeader header, IReadOnlyList<InvoiceLineRow> lines, bool isReturn, CancellationToken cancellationToken)
    {
        var existing = await connection.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT status FROM erpnext_sync_log WHERE local_entity_type = @Entity AND local_entity_id = @invoiceId AND erpnext_doctype = @CogsDoctype;",
            new { Entity, invoiceId, CogsDoctype },
            cancellationToken: cancellationToken));
        if (existing is ErpNextSyncLogWriter.Synced or ErpNextSyncLogWriter.Cancelled or ErpNextSyncLogWriter.Skipped && _erpNextClient.IsEnabled)
        {
            return;
        }

        var amount = Math.Round(lines.Sum(l => l.Quantity * l.LineCostUsd), 4);
        if (amount <= 0)
        {
            await ErpNextSyncLogWriter.WriteAsync(connection, Entity, invoiceId, CogsDoctype, null, ErpNextSyncLogWriter.Skipped,
                "No cost recorded on the invoice lines, so no cost-of-goods entry was booked.", cancellationToken);
            return;
        }

        var result = await _erpNextClient.SyncCogsEntryAsync(
            new ErpNextCogsEntrySync(invoiceId, header.InvoiceNumber ?? invoiceId.ToString(), header.InvoiceDate, amount, isReturn),
            cancellationToken);

        await ErpNextSyncLogWriter.WriteAsync(
            connection, Entity, invoiceId, CogsDoctype,
            result.IsSuccess ? result.Value : null,
            _erpNextClient.IsEnabled ? (result.IsSuccess ? ErpNextSyncLogWriter.Synced : ErpNextSyncLogWriter.Failed) : ErpNextSyncLogWriter.Skipped,
            result.IsFailure ? result.Error.Message : null,
            cancellationToken);
    }

    /// <summary>Cancels the Sales Invoice and its cost entry in ERPNext after a local void. Nothing to do if it never reached ERPNext.</summary>
    public async Task CancelAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        if (!_erpNextClient.IsEnabled)
        {
            return;
        }

        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);

        foreach (var doctype in new[] { CogsDoctype, Doctype })
        {
            var name = await ErpNextSyncLogWriter.FindSyncedNameAsync(connection, Entity, invoiceId, doctype, cancellationToken);
            if (name is null)
            {
                continue;
            }

            var result = await _erpNextClient.CancelDocumentAsync(doctype, name, cancellationToken);
            await ErpNextSyncLogWriter.WriteAsync(
                connection, Entity, invoiceId, doctype, name,
                result.IsSuccess ? ErpNextSyncLogWriter.Cancelled : ErpNextSyncLogWriter.Failed,
                result.IsFailure ? $"Cancel failed: {result.Error.Message}" : null,
                cancellationToken);
        }
    }

    /// <returns>The customer's ERPNext record name, once the customer, sales person and items all exist in ERPNext.</returns>
    private async Task<Result<string>> EnsureMasterDataAsync(DbConnection connection, InvoiceHeader header, IReadOnlyList<InvoiceLineRow> lines, CancellationToken cancellationToken)
    {
        var customer = await ErpNextPartyLinks.EnsureAsync(connection, _erpNextClient, header.PartyId, PartyTypeCodes.Customer, cancellationToken);
        if (customer.IsFailure)
        {
            return Result<string>.Failure(customer.Error);
        }

        if (header.SalesRepId is { } repId && header.SalesRepName is { } repName)
        {
            var person = await _erpNextClient.SyncSalesPersonAsync(new ErpNextSalesPersonSync(repId, repName, header.SalesRepCommission, header.SalesRepActive), cancellationToken);
            await ErpNextSyncLogWriter.WriteAsync(connection, "SalesRep", repId, "Sales Person", person.IsSuccess ? person.Value : null,
                _erpNextClient.IsEnabled ? (person.IsSuccess ? ErpNextSyncLogWriter.Synced : ErpNextSyncLogWriter.Failed) : ErpNextSyncLogWriter.Skipped,
                person.IsFailure ? person.Error.Message : null, cancellationToken);
            if (person.IsFailure)
            {
                return Result<string>.Failure(new Error("ErpNext.SalesPersonSync", $"Sales person '{repName}' could not be created in ERPNext: {person.Error.Message}"));
            }
        }

        foreach (var item in lines.DistinctBy(l => l.SkuId))
        {
            var synced = await _erpNextClient.SyncItemAsync(new ErpNextItemSync(item.SkuId, item.ItemCode, item.NameEn, item.NameAr, item.CostPrice, item.SellingPrice), cancellationToken);
            await ErpNextSyncLogWriter.WriteAsync(connection, "Sku", item.SkuId, "Item", synced.IsSuccess ? synced.Value : null,
                _erpNextClient.IsEnabled ? (synced.IsSuccess ? ErpNextSyncLogWriter.Synced : ErpNextSyncLogWriter.Failed) : ErpNextSyncLogWriter.Skipped,
                synced.IsFailure ? synced.Error.Message : null, cancellationToken);
            if (synced.IsFailure)
            {
                return Result<string>.Failure(new Error("ErpNext.ItemSync", $"Item '{item.ItemCode}' could not be created in ERPNext: {synced.Error.Message}"));
            }
        }

        return Result<string>.Success(customer.Value!);
    }

    private sealed record InvoiceHeader(
        string? InvoiceNumber, string Type, Guid? OriginalInvoiceId, Guid PartyId, string CustomerName, string? TaxNumber, DateOnly InvoiceDate, DateOnly DueDate, decimal DiscountAmountUsd, decimal DeliveryFeeUsd,
        Guid? SalesRepId, string? SalesRepName, decimal SalesRepCommission, bool SalesRepActive);

    private sealed record InvoiceLineRow(
        Guid SkuId, string ItemCode, string NameEn, string NameAr, decimal CostPrice, decimal SellingPrice,
        decimal Quantity, decimal UnitPrice, decimal DiscountPercent, decimal LineCostUsd);
}
