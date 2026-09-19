using AutoPartsERP.Infrastructure.Services;

namespace AutoPartsERP.Infrastructure.Workers.OutboxDispatcher;

/// <summary>A voided invoice must leave the ledger: cancel its Sales Invoice in ERPNext (the local credit note is not sent).</summary>
public sealed class InvoiceVoidedOutboxHandler : IOutboxEventHandler
{
    private readonly SalesInvoiceErpNextSyncer _erpNextSyncer;

    public InvoiceVoidedOutboxHandler(SalesInvoiceErpNextSyncer erpNextSyncer)
    {
        _erpNextSyncer = erpNextSyncer;
    }

    public string EventType => OutboxEventTypes.InvoiceVoided;

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<InvoiceVoidedPayload>(message.PayloadJson);
        if (payload is not null)
        {
            await _erpNextSyncer.CancelAsync(payload.InvoiceId, cancellationToken);
        }
    }
}
