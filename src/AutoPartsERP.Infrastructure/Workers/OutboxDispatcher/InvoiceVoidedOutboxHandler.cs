using AutoPartsERP.Infrastructure.Services;

namespace AutoPartsERP.Infrastructure.Workers.OutboxDispatcher;

/// <summary>A voided invoice must leave the ledger: cancel its Sales Invoice in ERPNext (the local credit note is not sent).</summary>
public sealed class InvoiceVoidedOutboxHandler : IOutboxEventHandler
{
    private readonly SalesInvoiceErpNextSyncer _erpNextSyncer;
    private readonly StockAlertService _stockAlerts;

    public InvoiceVoidedOutboxHandler(SalesInvoiceErpNextSyncer erpNextSyncer, StockAlertService stockAlerts)
    {
        _erpNextSyncer = erpNextSyncer;
        _stockAlerts = stockAlerts;
    }

    public string EventType => OutboxEventTypes.InvoiceVoided;

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<InvoiceVoidedPayload>(message.PayloadJson);
        if (payload is not null)
        {
            // The void put the goods back: alerts for them may resolve (or a voided return may raise one).
            await _stockAlerts.ScanInvoiceAsync(payload.InvoiceId, cancellationToken);
            await _erpNextSyncer.CancelAsync(payload.InvoiceId, cancellationToken);
        }
    }
}
