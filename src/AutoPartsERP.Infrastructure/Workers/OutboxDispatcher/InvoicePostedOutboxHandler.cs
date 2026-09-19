using AutoPartsERP.Infrastructure.Services;

namespace AutoPartsERP.Infrastructure.Workers.OutboxDispatcher;

public sealed class InvoicePostedOutboxHandler : IOutboxEventHandler
{
    private readonly ILogger<InvoicePostedOutboxHandler> _logger;
    private readonly ErpMetrics _metrics;
    private readonly SalesInvoiceErpNextSyncer _syncer;

    public InvoicePostedOutboxHandler(
        ILogger<InvoicePostedOutboxHandler> logger,
        ErpMetrics metrics,
        SalesInvoiceErpNextSyncer syncer)
    {
        _logger = logger;
        _metrics = metrics;
        _syncer = syncer;
    }

    public string EventType => OutboxEventTypes.InvoicePosted;

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<InvoicePostedPayload>(message.PayloadJson);
        if (payload is null)
        {
            return;
        }

        _metrics.RecordInvoicePosted(payload.TotalSyp);
        _logger.LogInformation(
            "Outbox InvoicePosted handled. InvoiceId={InvoiceId} CorrelationId={CorrelationId}",
            payload.InvoiceId,
            message.CorrelationId);

        // Accounting hand-off: the syncer no-ops into a SKIPPED log row while ERPNext is disabled.
        await _syncer.SyncAsync(payload.InvoiceId, cancellationToken);
    }
}
