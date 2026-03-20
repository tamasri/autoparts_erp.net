namespace AutoPartsERP.Infrastructure.Workers.OutboxDispatcher;

public sealed class InvoicePostedOutboxHandler : IOutboxEventHandler
{
    private readonly ILogger<InvoicePostedOutboxHandler> _logger;
    private readonly ErpMetrics _metrics;

    public InvoicePostedOutboxHandler(ILogger<InvoicePostedOutboxHandler> logger, ErpMetrics metrics)
    {
        _logger = logger;
        _metrics = metrics;
    }

    public string EventType => OutboxEventTypes.InvoicePosted;

    public Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<InvoicePostedPayload>(message.PayloadJson);
        if (payload is not null)
        {
            _metrics.RecordInvoicePosted(payload.TotalSyp);
            _logger.LogInformation(
                "Outbox InvoicePosted handled. InvoiceId={InvoiceId} CorrelationId={CorrelationId}",
                payload.InvoiceId,
                message.CorrelationId);
        }

        return Task.CompletedTask;
    }
}
