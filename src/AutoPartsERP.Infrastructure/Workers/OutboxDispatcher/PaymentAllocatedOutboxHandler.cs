namespace AutoPartsERP.Infrastructure.Workers.OutboxDispatcher;

public sealed class PaymentAllocatedOutboxHandler : IOutboxEventHandler
{
    private readonly ILogger<PaymentAllocatedOutboxHandler> _logger;
    private readonly ErpMetrics _metrics;

    public PaymentAllocatedOutboxHandler(ILogger<PaymentAllocatedOutboxHandler> logger, ErpMetrics metrics)
    {
        _logger = logger;
        _metrics = metrics;
    }

    public string EventType => OutboxEventTypes.PaymentAllocated;

    public Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<PaymentAllocatedPayload>(message.PayloadJson);
        if (payload is not null)
        {
            _metrics.RecordPaymentReceived();
            _logger.LogInformation(
                "Outbox PaymentAllocated handled. PaymentId={PaymentId} InvoiceId={InvoiceId} CorrelationId={CorrelationId}",
                payload.PaymentId,
                payload.InvoiceId,
                message.CorrelationId);
        }

        return Task.CompletedTask;
    }
}
