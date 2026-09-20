using AutoPartsERP.Infrastructure.Services;

namespace AutoPartsERP.Infrastructure.Workers.OutboxDispatcher;

/// <summary>A posted supplier bill becomes a Purchase Invoice in ERPNext.</summary>
public sealed class PurchaseInvoicePostedOutboxHandler : IOutboxEventHandler
{
    private readonly PurchaseErpNextSyncer _syncer;

    public PurchaseInvoicePostedOutboxHandler(PurchaseErpNextSyncer syncer) { _syncer = syncer; }

    public string EventType => OutboxEventTypes.PurchaseInvoicePosted;

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<PurchaseInvoiceEventPayload>(message.PayloadJson);
        if (payload is not null) await _syncer.SyncInvoiceAsync(payload.PurchaseInvoiceId, cancellationToken);
    }
}

/// <summary>A voided bill must leave the ledger.</summary>
public sealed class PurchaseInvoiceVoidedOutboxHandler : IOutboxEventHandler
{
    private readonly PurchaseErpNextSyncer _syncer;

    public PurchaseInvoiceVoidedOutboxHandler(PurchaseErpNextSyncer syncer) { _syncer = syncer; }

    public string EventType => OutboxEventTypes.PurchaseInvoiceVoided;

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<PurchaseInvoiceEventPayload>(message.PayloadJson);
        if (payload is not null) await _syncer.CancelInvoiceAsync(payload.PurchaseInvoiceId, cancellationToken);
    }
}

/// <summary>A supplier payment becomes a Payment Entry (Pay) that settles the bills it was allocated to.</summary>
public sealed class SupplierPaymentCreatedOutboxHandler : IOutboxEventHandler
{
    private readonly PurchaseErpNextSyncer _syncer;

    public SupplierPaymentCreatedOutboxHandler(PurchaseErpNextSyncer syncer) { _syncer = syncer; }

    public string EventType => OutboxEventTypes.SupplierPaymentCreated;

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SupplierPaymentEventPayload>(message.PayloadJson);
        if (payload is not null) await _syncer.SyncPaymentAsync(payload.SupplierPaymentId, cancellationToken);
    }
}

/// <summary>A reversed supplier payment is cancelled in ERPNext.</summary>
public sealed class SupplierPaymentReversedOutboxHandler : IOutboxEventHandler
{
    private readonly PurchaseErpNextSyncer _syncer;

    public SupplierPaymentReversedOutboxHandler(PurchaseErpNextSyncer syncer) { _syncer = syncer; }

    public string EventType => OutboxEventTypes.SupplierPaymentReversed;

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<SupplierPaymentEventPayload>(message.PayloadJson);
        if (payload is not null) await _syncer.CancelPaymentAsync(payload.SupplierPaymentId, cancellationToken);
    }
}

/// <summary>A posted stock adjustment is booked in ERPNext at its cost value.</summary>
public sealed class StockAdjustmentPostedOutboxHandler : IOutboxEventHandler
{
    private readonly StockAdjustmentErpNextSyncer _syncer;

    public StockAdjustmentPostedOutboxHandler(StockAdjustmentErpNextSyncer syncer) { _syncer = syncer; }

    public string EventType => OutboxEventTypes.StockAdjustmentPosted;

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<StockAdjustmentPostedPayload>(message.PayloadJson);
        if (payload is not null) await _syncer.SyncAsync(payload.StockAdjustmentId, cancellationToken);
    }
}
