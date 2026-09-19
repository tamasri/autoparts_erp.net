namespace AutoPartsERP.Infrastructure.Services;

/// <summary>
/// Pushes a posted sales invoice to ERPNext and cancels it there when it is voided. It first makes sure the customer and the
/// items exist in ERPNext (idempotent upserts), so a brand-new customer or item no longer makes the invoice fail until the
/// next scheduled catalog sync. Names, not ids: customer = party display name, item_code = sku code.
/// Every outcome is recorded in erpnext_sync_log.
/// </summary>
public sealed class SalesInvoiceErpNextSyncer
{
    private const string Entity = "Invoice";
    private const string Doctype = "Sales Invoice";

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

        if (await ErpNextSyncLogWriter.FindSyncedNameAsync(connection, Entity, invoiceId, Doctype, cancellationToken) is not null)
        {
            return;
        }

        var header = await connection.QuerySingleOrDefaultAsync<InvoiceHeader>(new CommandDefinition(
            """
            SELECT i.invoice_number AS InvoiceNumber, p.id AS PartyId, p.display_name AS CustomerName, p.tax_number AS TaxNumber,
                   i.invoice_date AS InvoiceDate, i.due_date AS DueDate
            FROM invoices i
            INNER JOIN customers c ON c.id = i.customer_id
            INNER JOIN parties p ON p.id = c.party_id
            WHERE i.id = @invoiceId AND i.status = 'POSTED' AND i.invoice_type = 'SALE';
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
                   l.quantity AS Quantity, l.unit_price_usd AS UnitPrice, l.discount_pct AS DiscountPercent
            FROM invoice_lines l
            INNER JOIN skus s ON s.id = l.sku_id
            WHERE l.invoice_id = @invoiceId
            ORDER BY l.line_number;
            """,
            new { invoiceId },
            cancellationToken: cancellationToken))).ToList();

        // Master data first: ERPNext rejects an invoice whose customer or items it has never seen.
        var prerequisite = await EnsureMasterDataAsync(connection, header, lines, cancellationToken);
        if (prerequisite.IsFailure)
        {
            await ErpNextSyncLogWriter.WriteAsync(connection, Entity, invoiceId, Doctype, null,
                _erpNextClient.IsEnabled ? ErpNextSyncLogWriter.Failed : ErpNextSyncLogWriter.Skipped, prerequisite.Error.Message, cancellationToken);
            return;
        }

        var result = await _erpNextClient.SyncSalesInvoiceAsync(
            new ErpNextSalesInvoiceSync(
                invoiceId,
                header.InvoiceNumber ?? invoiceId.ToString(),
                header.CustomerName,
                header.InvoiceDate,
                header.DueDate,
                lines.Select(l => new ErpNextInvoiceLineSync(l.ItemCode, l.Quantity, l.UnitPrice, l.DiscountPercent)).ToList()),
            cancellationToken);

        await ErpNextSyncLogWriter.WriteAsync(
            connection, Entity, invoiceId, Doctype,
            result.IsSuccess ? result.Value : null,
            _erpNextClient.IsEnabled ? (result.IsSuccess ? ErpNextSyncLogWriter.Synced : ErpNextSyncLogWriter.Failed) : ErpNextSyncLogWriter.Skipped,
            result.IsFailure ? result.Error.Message : null,
            cancellationToken);
    }

    /// <summary>Cancels the Sales Invoice in ERPNext after a local void. Nothing to do if it never reached ERPNext.</summary>
    public async Task CancelAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        if (!_erpNextClient.IsEnabled)
        {
            return;
        }

        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        var name = await ErpNextSyncLogWriter.FindSyncedNameAsync(connection, Entity, invoiceId, Doctype, cancellationToken);
        if (name is null)
        {
            return;
        }

        var result = await _erpNextClient.CancelDocumentAsync(Doctype, name, cancellationToken);
        await ErpNextSyncLogWriter.WriteAsync(
            connection, Entity, invoiceId, Doctype, name,
            result.IsSuccess ? ErpNextSyncLogWriter.Cancelled : ErpNextSyncLogWriter.Failed,
            result.IsFailure ? $"Cancel failed: {result.Error.Message}" : null,
            cancellationToken);
    }

    private async Task<Result> EnsureMasterDataAsync(DbConnection connection, InvoiceHeader header, IReadOnlyList<InvoiceLineRow> lines, CancellationToken cancellationToken)
    {
        var customer = await _erpNextClient.SyncPartyAsync(new ErpNextPartySync(header.PartyId, header.CustomerName, PartyTypeCodes.Customer, header.TaxNumber), cancellationToken);
        await ErpNextSyncLogWriter.WriteAsync(connection, "Party", header.PartyId, "Customer", customer.IsSuccess ? customer.Value : null,
            _erpNextClient.IsEnabled ? (customer.IsSuccess ? ErpNextSyncLogWriter.Synced : ErpNextSyncLogWriter.Failed) : ErpNextSyncLogWriter.Skipped,
            customer.IsFailure ? customer.Error.Message : null, cancellationToken);
        if (customer.IsFailure)
        {
            return Result.Failure(new Error("ErpNext.CustomerSync", $"Customer '{header.CustomerName}' could not be created in ERPNext: {customer.Error.Message}"));
        }

        foreach (var item in lines.DistinctBy(l => l.SkuId))
        {
            var synced = await _erpNextClient.SyncItemAsync(new ErpNextItemSync(item.SkuId, item.ItemCode, item.NameEn, item.NameAr, item.CostPrice, item.SellingPrice), cancellationToken);
            await ErpNextSyncLogWriter.WriteAsync(connection, "Sku", item.SkuId, "Item", synced.IsSuccess ? synced.Value : null,
                _erpNextClient.IsEnabled ? (synced.IsSuccess ? ErpNextSyncLogWriter.Synced : ErpNextSyncLogWriter.Failed) : ErpNextSyncLogWriter.Skipped,
                synced.IsFailure ? synced.Error.Message : null, cancellationToken);
            if (synced.IsFailure)
            {
                return Result.Failure(new Error("ErpNext.ItemSync", $"Item '{item.ItemCode}' could not be created in ERPNext: {synced.Error.Message}"));
            }
        }

        return Result.Success();
    }

    private sealed record InvoiceHeader(string? InvoiceNumber, Guid PartyId, string CustomerName, string? TaxNumber, DateOnly InvoiceDate, DateOnly DueDate);

    private sealed record InvoiceLineRow(
        Guid SkuId, string ItemCode, string NameEn, string NameAr, decimal CostPrice, decimal SellingPrice,
        decimal Quantity, decimal UnitPrice, decimal DiscountPercent);
}
