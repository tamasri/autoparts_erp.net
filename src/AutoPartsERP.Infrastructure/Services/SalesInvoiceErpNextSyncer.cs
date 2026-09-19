namespace AutoPartsERP.Infrastructure.Services;

/// <summary>
/// Loads a posted sales invoice from our own tables, translates it into the names ERPNext knows
/// (customer = the party display name SyncCatalogToErpNextJob registered, item_code = sku code) and
/// pushes it, recording the outcome in erpnext_sync_log. Shared by the InvoicePosted outbox handler
/// (immediate sync) and SyncCatalogToErpNextJob (backlog and retry of FAILED syncs).
/// </summary>
public sealed class SalesInvoiceErpNextSyncer
{
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

        var alreadySynced = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM erpnext_sync_log WHERE local_entity_type = 'Invoice' AND local_entity_id = @invoiceId AND erpnext_doctype = 'Sales Invoice' AND status = 'SYNCED');",
            new { invoiceId },
            cancellationToken: cancellationToken));
        if (alreadySynced)
        {
            return;
        }

        var header = await connection.QuerySingleOrDefaultAsync<InvoiceHeader>(new CommandDefinition(
            """
            SELECT i.invoice_number AS InvoiceNumber, p.display_name AS CustomerName,
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
            SELECT s.code AS ItemCode, l.quantity AS Quantity, l.unit_price_usd AS UnitPrice, l.discount_pct AS DiscountPercent
            FROM invoice_lines l
            INNER JOIN skus s ON s.id = l.sku_id
            WHERE l.invoice_id = @invoiceId
            ORDER BY l.line_number;
            """,
            new { invoiceId },
            cancellationToken: cancellationToken))).ToList();

        var result = await _erpNextClient.SyncSalesInvoiceAsync(
            new ErpNextSalesInvoiceSync(
                invoiceId,
                header.InvoiceNumber ?? invoiceId.ToString(),
                header.CustomerName,
                header.InvoiceDate,
                header.DueDate,
                lines.Select(l => new ErpNextInvoiceLineSync(l.ItemCode, l.Quantity, l.UnitPrice, l.DiscountPercent)).ToList()),
            cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO erpnext_sync_log (id, local_entity_type, local_entity_id, erpnext_doctype, erpnext_name, status, last_error, attempt_count, synced_at, created_at, updated_at)
            VALUES (uuid_generate_v4(), 'Invoice', @invoiceId, 'Sales Invoice', @ErpNextName, @Status, @LastError, 1, @SyncedAt, now(), now())
            ON CONFLICT (local_entity_type, local_entity_id, erpnext_doctype) DO UPDATE
                SET erpnext_name = EXCLUDED.erpnext_name,
                    status = EXCLUDED.status,
                    last_error = EXCLUDED.last_error,
                    attempt_count = erpnext_sync_log.attempt_count + 1,
                    synced_at = EXCLUDED.synced_at,
                    updated_at = now();
            """,
            new
            {
                invoiceId,
                ErpNextName = result.IsSuccess ? result.Value : null,
                Status = _erpNextClient.IsEnabled ? (result.IsSuccess ? "SYNCED" : "FAILED") : "SKIPPED",
                LastError = result.IsFailure ? result.Error.Message : null,
                SyncedAt = result.IsSuccess ? DateTimeOffset.UtcNow : (DateTimeOffset?)null
            },
            cancellationToken: cancellationToken));
    }

    private sealed record InvoiceHeader(string? InvoiceNumber, string CustomerName, DateOnly InvoiceDate, DateOnly DueDate);

    private sealed record InvoiceLineRow(string ItemCode, decimal Quantity, decimal UnitPrice, decimal DiscountPercent);
}
