namespace AutoPartsERP.Infrastructure.Services;

/// <summary>
/// Pushes customer receipts to ERPNext as Payment Entries that settle the Sales Invoices they were allocated to, and cancels
/// them when a payment is reversed. Amounts are USD (the ERPNext company currency); a SYP-only receipt is converted with the
/// payment's own FX rate. A payment is synced once its invoices are in ERPNext; until then the attempt is logged as FAILED and
/// the scheduled sweep retries it.
/// </summary>
public sealed class PaymentErpNextSyncer
{
    private const string Entity = "Payment";
    private const string Doctype = "Payment Entry";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IErpNextClient _erpNextClient;
    private readonly SalesInvoiceErpNextSyncer _invoiceSyncer;

    public PaymentErpNextSyncer(IDbConnectionFactory connectionFactory, IErpNextClient erpNextClient, SalesInvoiceErpNextSyncer invoiceSyncer)
    {
        _connectionFactory = connectionFactory;
        _erpNextClient = erpNextClient;
        _invoiceSyncer = invoiceSyncer;
    }

    public async Task SyncAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);

        if (await ErpNextSyncLogWriter.FindSyncedNameAsync(connection, Entity, paymentId, Doctype, cancellationToken) is not null)
        {
            return;
        }

        var payment = await connection.QuerySingleOrDefaultAsync<PaymentRow>(new CommandDefinition(
            """
            SELECT p.id AS Id, pa.id AS PartyId, pa.display_name AS CustomerName, pa.tax_number AS TaxNumber,
                   p.payment_date AS PaymentDate, p.payment_method AS PaymentMethod, p.reference_number AS ReferenceNumber,
                   p.amount_usd AS AmountUsd, p.amount_syp AS AmountSyp, f.mid_rate AS FxMid
            FROM payments p
            INNER JOIN customers c ON c.id = p.customer_id
            INNER JOIN parties pa ON pa.id = c.party_id
            INNER JOIN fx_rates f ON f.id = p.fx_rate_id
            WHERE p.id = @paymentId AND p.payment_type = 'RECEIPT' AND p.is_reversed = FALSE;
            """,
            new { paymentId },
            cancellationToken: cancellationToken));
        if (payment is null)
        {
            return;
        }

        var allocations = (await connection.QueryAsync<AllocationRow>(new CommandDefinition(
            "SELECT invoice_id AS InvoiceId, allocated_usd AS AllocatedUsd, allocated_syp AS AllocatedSyp FROM payment_allocations WHERE payment_id = @paymentId;",
            new { paymentId },
            cancellationToken: cancellationToken))).ToList();

        var references = new List<ErpNextPaymentReference>();
        foreach (var allocation in allocations)
        {
            var invoiceName = await ErpNextSyncLogWriter.FindSyncedNameAsync(connection, "Invoice", allocation.InvoiceId, "Sales Invoice", cancellationToken);
            if (invoiceName is null && _erpNextClient.IsEnabled)
            {
                // The invoice may simply not have been pushed yet; try now before giving up.
                await _invoiceSyncer.SyncAsync(allocation.InvoiceId, cancellationToken);
                invoiceName = await ErpNextSyncLogWriter.FindSyncedNameAsync(connection, "Invoice", allocation.InvoiceId, "Sales Invoice", cancellationToken);
            }

            if (invoiceName is null)
            {
                await ErpNextSyncLogWriter.WriteAsync(connection, Entity, paymentId, Doctype, null,
                    _erpNextClient.IsEnabled ? ErpNextSyncLogWriter.Failed : ErpNextSyncLogWriter.Skipped,
                    "An invoice this payment settles is not in ERPNext yet; it will be retried.", cancellationToken);
                return;
            }

            references.Add(new ErpNextPaymentReference(invoiceName, ToUsd(allocation.AllocatedUsd, allocation.AllocatedSyp, payment.FxMid)));
        }

        var customer = await ErpNextPartyLinks.EnsureAsync(connection, _erpNextClient, payment.PartyId, PartyTypeCodes.Customer, cancellationToken);
        if (customer.IsFailure)
        {
            await ErpNextSyncLogWriter.WriteAsync(connection, Entity, paymentId, Doctype, null, ErpNextSyncLogWriter.Failed, customer.Error.Message, cancellationToken);
            return;
        }

        var result = await _erpNextClient.SyncPaymentAsync(
            new ErpNextPaymentSync(
                paymentId,
                customer.Value!, // the customer's ERPNext record, never its display name
                ToUsd(payment.AmountUsd, payment.AmountSyp, payment.FxMid),
                payment.PaymentDate,
                payment.PaymentMethod,
                payment.ReferenceNumber,
                references),
            cancellationToken);

        await ErpNextSyncLogWriter.WriteAsync(
            connection, Entity, paymentId, Doctype,
            result.IsSuccess ? result.Value : null,
            _erpNextClient.IsEnabled ? (result.IsSuccess ? ErpNextSyncLogWriter.Synced : ErpNextSyncLogWriter.Failed) : ErpNextSyncLogWriter.Skipped,
            result.IsFailure ? result.Error.Message : null,
            cancellationToken);
    }

    /// <summary>Immediate path used right after an allocation: sends the receipt only once nothing is left unallocated.</summary>
    public async Task SyncIfSettledAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        var settled = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT COALESCE((SELECT unallocated_usd <= 0 FROM payments WHERE id = @paymentId), FALSE);",
            new { paymentId },
            cancellationToken: cancellationToken));
        if (settled)
        {
            await SyncAsync(paymentId, cancellationToken);
        }
    }

    /// <summary>Cancels the Payment Entry after a local reversal. Nothing to do if it never reached ERPNext.</summary>
    public async Task CancelAsync(Guid paymentId, CancellationToken cancellationToken)
    {
        if (!_erpNextClient.IsEnabled)
        {
            return;
        }

        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        var name = await ErpNextSyncLogWriter.FindSyncedNameAsync(connection, Entity, paymentId, Doctype, cancellationToken);
        if (name is null)
        {
            return;
        }

        var result = await _erpNextClient.CancelDocumentAsync(Doctype, name, cancellationToken);
        await ErpNextSyncLogWriter.WriteAsync(
            connection, Entity, paymentId, Doctype, name,
            result.IsSuccess ? ErpNextSyncLogWriter.Cancelled : ErpNextSyncLogWriter.Failed,
            result.IsFailure ? $"Cancel failed: {result.Error.Message}" : null,
            cancellationToken);
    }

    /// <summary>
    /// Receipts that are ready to send: fully allocated, or old enough that an unallocated remainder is a genuine advance.
    /// (Syncing a receipt before it is allocated would record it in ERPNext without the invoices it settles.)
    /// </summary>
    public async Task<IReadOnlyList<Guid>> FindPendingAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        return (await connection.QueryAsync<Guid>(new CommandDefinition(
            """
            SELECT p.id
            FROM payments p
            WHERE p.payment_type = 'RECEIPT' AND p.is_reversed = FALSE
              AND (p.unallocated_usd <= 0 OR p.created_at < now() - interval '1 day')
              AND NOT EXISTS (SELECT 1 FROM erpnext_sync_log l
                              WHERE l.local_entity_type = 'Payment' AND l.local_entity_id = p.id
                                AND l.erpnext_doctype = 'Payment Entry' AND l.status IN ('SYNCED', 'CANCELLED'))
            ORDER BY p.payment_date, p.created_at;
            """,
            cancellationToken: cancellationToken))).ToList();
    }

    private static decimal ToUsd(decimal usd, decimal syp, decimal fxMid) =>
        usd > 0 ? usd : (fxMid > 0 ? Math.Round(syp / fxMid, 4) : 0);

    private sealed record PaymentRow(
        Guid Id, Guid PartyId, string CustomerName, string? TaxNumber, DateOnly PaymentDate, string PaymentMethod,
        string? ReferenceNumber, decimal AmountUsd, decimal AmountSyp, decimal FxMid);

    private sealed record AllocationRow(Guid InvoiceId, decimal AllocatedUsd, decimal AllocatedSyp);
}
