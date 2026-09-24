using AutoPartsERP.Application.Features.Purchasing.LandedCost;

namespace AutoPartsERP.Infrastructure.Services;

/// <summary>
/// Hands supplier bills and supplier payments to ERPNext (Purchase Invoice, Payment Entry "Pay") and cancels them when they are
/// voided or reversed. Like the sales side, every step is logged in erpnext_sync_log and a failed one is retried by the sweep.
/// A payment is only sent once the bills it settles are in ERPNext (it references them by name).
/// </summary>
public sealed class PurchaseErpNextSyncer
{
    private const string InvoiceEntity = "PurchaseInvoice";
    private const string InvoiceDoctype = "Purchase Invoice";
    private const string PaymentEntity = "SupplierPayment";
    private const string PaymentDoctype = "Payment Entry";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IErpNextClient _erpNextClient;

    public PurchaseErpNextSyncer(IDbConnectionFactory connectionFactory, IErpNextClient erpNextClient)
    {
        _connectionFactory = connectionFactory;
        _erpNextClient = erpNextClient;
    }

    private string StatusOf(bool succeeded) =>
        _erpNextClient.IsEnabled ? (succeeded ? ErpNextSyncLogWriter.Synced : ErpNextSyncLogWriter.Failed) : ErpNextSyncLogWriter.Skipped;

    // ------------------------------------------------------------------ purchase invoice

    public async Task SyncInvoiceAsync(Guid billId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        if (await ErpNextSyncLogWriter.FindSyncedNameAsync(connection, InvoiceEntity, billId, InvoiceDoctype, cancellationToken) is not null)
        {
            return;
        }

        var bill = await connection.QuerySingleOrDefaultAsync<BillRow>(new CommandDefinition(
            """
            SELECT p.bill_number AS BillNumber, pa.id AS PartyId, COALESCE(NULLIF(pa.display_name, ''), pa.display_name_ar) AS SupplierName,
                   pa.tax_number AS TaxNumber, p.bill_date AS BillDate, p.due_date AS DueDate, p.discount_amount_usd AS DiscountAmountUsd,
                   p.is_return AS IsReturn, p.return_against_id AS ReturnAgainstId, p.kind AS Kind
            FROM purchase_invoices p INNER JOIN parties pa ON pa.id = p.supplier_party_id
            WHERE p.id = @billId AND p.status = 'POSTED';
            """,
            new { billId }, cancellationToken: cancellationToken));
        if (bill is null)
        {
            return;
        }

        // A goods bill's lines are items; a landed-cost service bill's lines are charges, sent as non-stock service items.
        var isService = bill.Kind == "SERVICE";
        var lines = isService ? [] : (await connection.QueryAsync<BillLineRow>(new CommandDefinition(
            """
            SELECT k.id AS SkuId, k.code AS ItemCode, k.name AS NameEn, k.name_ar AS NameAr, k.cost_price_usd AS CostPrice, k.selling_price_usd AS SellingPrice,
                   l.quantity AS Quantity, l.unit_cost_usd AS UnitCost, l.discount_pct AS DiscountPercent
            FROM purchase_invoice_lines l
            INNER JOIN items i ON i.id = l.item_id
            INNER JOIN skus k ON k.id = i.sku_id
            WHERE l.purchase_invoice_id = @billId ORDER BY l.line_number;
            """,
            new { billId }, cancellationToken: cancellationToken))).ToList();
        var erpLines = isService
            ? (await connection.QueryAsync<(string ChargeType, decimal Amount)>(new CommandDefinition(
                "SELECT charge_type AS ChargeType, line_total_usd AS Amount FROM purchase_invoice_lines WHERE purchase_invoice_id = @billId ORDER BY line_number;",
                new { billId }, cancellationToken: cancellationToken)))
                .Select(c => new ErpNextInvoiceLineSync(LandedCostCharges.ServiceItemCode(c.ChargeType), 1, c.Amount, 0)).ToList()
            : lines.Select(l => new ErpNextInvoiceLineSync(l.ItemCode, l.Quantity, l.UnitCost, l.DiscountPercent)).ToList();

        // A return names the bill it returns (ERPNext return_against), so that bill goes first.
        string? returnAgainst = null;
        if (bill.ReturnAgainstId is { } againstId)
        {
            returnAgainst = await ErpNextSyncLogWriter.FindSyncedNameAsync(connection, InvoiceEntity, againstId, InvoiceDoctype, cancellationToken);
            if (returnAgainst is null && _erpNextClient.IsEnabled)
            {
                await SyncInvoiceAsync(againstId, cancellationToken);
                returnAgainst = await ErpNextSyncLogWriter.FindSyncedNameAsync(connection, InvoiceEntity, againstId, InvoiceDoctype, cancellationToken);
            }

            if (returnAgainst is null && _erpNextClient.IsEnabled)
            {
                await ErpNextSyncLogWriter.WriteAsync(connection, InvoiceEntity, billId, InvoiceDoctype, null, StatusOf(false),
                    "The returned bill is not in ERPNext yet; the return will be retried.", cancellationToken);
                return;
            }
        }

        var prerequisite = await EnsureMasterDataAsync(connection, bill, lines, cancellationToken);
        if (prerequisite.IsFailure)
        {
            await ErpNextSyncLogWriter.WriteAsync(connection, InvoiceEntity, billId, InvoiceDoctype, null, StatusOf(false), prerequisite.Error.Message, cancellationToken);
            return;
        }

        var result = await _erpNextClient.SyncPurchaseInvoiceAsync(
            new ErpNextPurchaseInvoiceSync(
                billId, bill.BillNumber, prerequisite.Value!, bill.BillDate, bill.DueDate, bill.IsReturn, erpLines, bill.DiscountAmountUsd, returnAgainst, isService),
            cancellationToken);

        await ErpNextSyncLogWriter.WriteAsync(connection, InvoiceEntity, billId, InvoiceDoctype, result.IsSuccess ? result.Value : null,
            StatusOf(result.IsSuccess), result.IsFailure ? result.Error.Message : null, cancellationToken);
    }

    public Task CancelInvoiceAsync(Guid billId, CancellationToken cancellationToken) => CancelAsync(InvoiceEntity, InvoiceDoctype, billId, cancellationToken);

    // ------------------------------------------------------------------ supplier payment

    public async Task SyncPaymentAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        if (await ErpNextSyncLogWriter.FindSyncedNameAsync(connection, PaymentEntity, paymentId, PaymentDoctype, cancellationToken) is not null)
        {
            return;
        }

        var payment = await connection.QuerySingleOrDefaultAsync<PaymentRow>(new CommandDefinition(
            """
            SELECT COALESCE(NULLIF(pa.display_name, ''), pa.display_name_ar) AS SupplierName, pa.id AS PartyId, pa.tax_number AS TaxNumber,
                   p.payment_date AS PaymentDate, p.payment_method AS PaymentMethod, p.reference_number AS ReferenceNumber, p.amount_usd AS Amount
            FROM supplier_payments p INNER JOIN parties pa ON pa.id = p.supplier_party_id
            WHERE p.id = @paymentId AND p.is_reversed = FALSE;
            """,
            new { paymentId }, cancellationToken: cancellationToken));
        if (payment is null)
        {
            return;
        }

        var allocations = (await connection.QueryAsync<(Guid BillId, decimal Amount)>(new CommandDefinition(
            "SELECT purchase_invoice_id AS BillId, allocated_usd AS Amount FROM supplier_payment_allocations WHERE supplier_payment_id = @paymentId;",
            new { paymentId }, cancellationToken: cancellationToken))).ToList();

        var references = new List<ErpNextPaymentReference>();
        foreach (var (billId, amount) in allocations)
        {
            var name = await ErpNextSyncLogWriter.FindSyncedNameAsync(connection, InvoiceEntity, billId, InvoiceDoctype, cancellationToken);
            if (name is null && _erpNextClient.IsEnabled)
            {
                await SyncInvoiceAsync(billId, cancellationToken);
                name = await ErpNextSyncLogWriter.FindSyncedNameAsync(connection, InvoiceEntity, billId, InvoiceDoctype, cancellationToken);
            }

            if (name is null)
            {
                await ErpNextSyncLogWriter.WriteAsync(connection, PaymentEntity, paymentId, PaymentDoctype, null, StatusOf(false),
                    "A bill this payment settles is not in ERPNext yet; it will be retried.", cancellationToken);
                return;
            }

            references.Add(new ErpNextPaymentReference(name, amount));
        }

        var supplier = await ErpNextPartyLinks.EnsureAsync(connection, _erpNextClient, payment.PartyId, PartyTypeCodes.Vendor, cancellationToken);
        if (supplier.IsFailure)
        {
            await ErpNextSyncLogWriter.WriteAsync(connection, PaymentEntity, paymentId, PaymentDoctype, null, StatusOf(false), supplier.Error.Message, cancellationToken);
            return;
        }

        // The supplier's ERPNext record, never its display name.
        var result = await _erpNextClient.SyncSupplierPaymentAsync(
            new ErpNextSupplierPaymentSync(paymentId, supplier.Value!, payment.Amount, payment.PaymentDate, payment.PaymentMethod, payment.ReferenceNumber, references),
            cancellationToken);
        await ErpNextSyncLogWriter.WriteAsync(connection, PaymentEntity, paymentId, PaymentDoctype, result.IsSuccess ? result.Value : null,
            StatusOf(result.IsSuccess), result.IsFailure ? result.Error.Message : null, cancellationToken);
    }

    /// <summary>Cancel the Payment Entry first (a bill with a live payment cannot be cancelled in ERPNext), then the bill.</summary>
    public Task CancelPaymentAsync(Guid paymentId, CancellationToken cancellationToken) => CancelAsync(PaymentEntity, PaymentDoctype, paymentId, cancellationToken);

    // ------------------------------------------------------------------ sweep

    /// <summary>Posted bills and unreversed payments that ERPNext does not have yet (retries anything that failed).</summary>
    public async Task<(IReadOnlyList<Guid> Bills, IReadOnlyList<Guid> Payments)> FindPendingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        var bills = (await connection.QueryAsync<Guid>(new CommandDefinition(
            """
            SELECT p.id FROM purchase_invoices p
            WHERE p.status = 'POSTED'
              AND NOT EXISTS (SELECT 1 FROM erpnext_sync_log l WHERE l.local_entity_type = 'PurchaseInvoice' AND l.local_entity_id = p.id
                                AND l.erpnext_doctype = 'Purchase Invoice' AND l.status IN ('SYNCED', 'CANCELLED'))
            ORDER BY p.bill_date, p.created_at;
            """,
            cancellationToken: cancellationToken))).ToList();
        var payments = (await connection.QueryAsync<Guid>(new CommandDefinition(
            """
            SELECT p.id FROM supplier_payments p
            WHERE p.is_reversed = FALSE
              AND NOT EXISTS (SELECT 1 FROM erpnext_sync_log l WHERE l.local_entity_type = 'SupplierPayment' AND l.local_entity_id = p.id
                                AND l.erpnext_doctype = 'Payment Entry' AND l.status IN ('SYNCED', 'CANCELLED'))
            ORDER BY p.payment_date, p.created_at;
            """,
            cancellationToken: cancellationToken))).ToList();
        return (bills, payments);
    }

    // ------------------------------------------------------------------ helpers

    private async Task CancelAsync(string entity, string doctype, Guid id, CancellationToken cancellationToken)
    {
        if (!_erpNextClient.IsEnabled)
        {
            return;
        }

        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        var name = await ErpNextSyncLogWriter.FindSyncedNameAsync(connection, entity, id, doctype, cancellationToken);
        if (name is null)
        {
            return;
        }

        var result = await _erpNextClient.CancelDocumentAsync(doctype, name, cancellationToken);
        await ErpNextSyncLogWriter.WriteAsync(connection, entity, id, doctype, name,
            result.IsSuccess ? ErpNextSyncLogWriter.Cancelled : ErpNextSyncLogWriter.Failed,
            result.IsFailure ? $"Cancel failed: {result.Error.Message}" : null, cancellationToken);
    }

    /// <returns>The supplier's ERPNext record name, once the supplier and the items exist in ERPNext.</returns>
    private async Task<Result<string>> EnsureMasterDataAsync(DbConnection connection, BillRow bill, IReadOnlyList<BillLineRow> lines, CancellationToken cancellationToken)
    {
        var supplier = await ErpNextPartyLinks.EnsureAsync(connection, _erpNextClient, bill.PartyId, PartyTypeCodes.Vendor, cancellationToken);
        if (supplier.IsFailure)
        {
            return Result<string>.Failure(supplier.Error);
        }

        foreach (var item in lines.DistinctBy(l => l.SkuId))
        {
            var synced = await _erpNextClient.SyncItemAsync(new ErpNextItemSync(item.SkuId, item.ItemCode, item.NameEn, item.NameAr, item.CostPrice, item.SellingPrice), cancellationToken);
            await ErpNextSyncLogWriter.WriteAsync(connection, "Sku", item.SkuId, "Item", synced.IsSuccess ? synced.Value : null,
                StatusOf(synced.IsSuccess), synced.IsFailure ? synced.Error.Message : null, cancellationToken);
            if (synced.IsFailure)
            {
                return Result<string>.Failure(new Error("ErpNext.ItemSync", $"Item '{item.ItemCode}' could not be created in ERPNext: {synced.Error.Message}"));
            }
        }

        return Result<string>.Success(supplier.Value!);
    }

    private sealed record BillRow(
        string BillNumber, Guid PartyId, string SupplierName, string? TaxNumber, DateOnly BillDate, DateOnly DueDate, decimal DiscountAmountUsd,
        bool IsReturn, Guid? ReturnAgainstId, string Kind);

    private sealed record BillLineRow(Guid SkuId, string ItemCode, string NameEn, string NameAr, decimal CostPrice, decimal SellingPrice, decimal Quantity, decimal UnitCost, decimal DiscountPercent);

    private sealed record PaymentRow(string SupplierName, Guid PartyId, string? TaxNumber, DateOnly PaymentDate, string PaymentMethod, string? ReferenceNumber, decimal Amount);
}
