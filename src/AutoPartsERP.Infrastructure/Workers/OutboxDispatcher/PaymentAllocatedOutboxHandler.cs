using AutoPartsERP.Infrastructure.Services;

namespace AutoPartsERP.Infrastructure.Workers.OutboxDispatcher;

public sealed class PaymentAllocatedOutboxHandler : IOutboxEventHandler
{
    private readonly ILogger<PaymentAllocatedOutboxHandler> _logger;
    private readonly ErpMetrics _metrics;
    private readonly PaymentErpNextSyncer _erpNextSyncer;

    public PaymentAllocatedOutboxHandler(ILogger<PaymentAllocatedOutboxHandler> logger, ErpMetrics metrics, PaymentErpNextSyncer erpNextSyncer)
    {
        _logger = logger;
        _metrics = metrics;
        _erpNextSyncer = erpNextSyncer;
    }

    public string EventType => OutboxEventTypes.PaymentAllocated;

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<PaymentAllocatedPayload>(message.PayloadJson);
        if (payload is null)
        {
            return;
        }

        _metrics.RecordPaymentReceived();
        _logger.LogInformation(
            "Outbox PaymentAllocated handled. PaymentId={PaymentId} InvoiceId={InvoiceId} CorrelationId={CorrelationId}",
            payload.PaymentId,
            payload.InvoiceId,
            message.CorrelationId);

        // Accounting hand-off: a fully allocated receipt becomes a Payment Entry in ERPNext (no-op / SKIPPED while ERPNext is disabled).
        await _erpNextSyncer.SyncIfSettledAsync(payload.PaymentId, cancellationToken);
    }
}
