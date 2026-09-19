using AutoPartsERP.Infrastructure.Services;

namespace AutoPartsERP.Infrastructure.Workers.OutboxDispatcher;

/// <summary>A reversed receipt must not stay booked in the ledger: cancel its Payment Entry in ERPNext.</summary>
public sealed class PaymentReversedOutboxHandler : IOutboxEventHandler
{
    private readonly PaymentErpNextSyncer _erpNextSyncer;

    public PaymentReversedOutboxHandler(PaymentErpNextSyncer erpNextSyncer)
    {
        _erpNextSyncer = erpNextSyncer;
    }

    public string EventType => OutboxEventTypes.PaymentReversed;

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<PaymentReversedPayload>(message.PayloadJson);
        if (payload is not null)
        {
            await _erpNextSyncer.CancelAsync(payload.PaymentId, cancellationToken);
        }
    }
}
