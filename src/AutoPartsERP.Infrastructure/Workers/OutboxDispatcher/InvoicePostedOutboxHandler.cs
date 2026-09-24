using AutoPartsERP.Infrastructure.Services;

namespace AutoPartsERP.Infrastructure.Workers.OutboxDispatcher;

public sealed class InvoicePostedOutboxHandler : IOutboxEventHandler
{
    private readonly ILogger<InvoicePostedOutboxHandler> _logger;
    private readonly ErpMetrics _metrics;
    private readonly SalesInvoiceErpNextSyncer _syncer;
    private readonly StockAlertService _stockAlerts;

    public InvoicePostedOutboxHandler(
        ILogger<InvoicePostedOutboxHandler> logger,
        ErpMetrics metrics,
        SalesInvoiceErpNextSyncer syncer,
        StockAlertService stockAlerts)
    {
        _stockAlerts = stockAlerts;
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

        // Stock alerts first: they must not wait for (or depend on) the accounting hand-off.
        await _stockAlerts.ScanInvoiceAsync(payload.InvoiceId, cancellationToken);

        // Accounting hand-off: the syncer no-ops into a SKIPPED log row while ERPNext is disabled.
        await _syncer.SyncAsync(payload.InvoiceId, cancellationToken);
    }
}
