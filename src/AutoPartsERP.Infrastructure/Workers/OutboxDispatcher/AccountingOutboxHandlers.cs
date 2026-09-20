using AutoPartsERP.Infrastructure.Services;

namespace AutoPartsERP.Infrastructure.Workers.OutboxDispatcher;

/// <summary>A posted manual entry becomes a Journal Entry in ERPNext.</summary>
public sealed class JournalEntryPostedOutboxHandler : IOutboxEventHandler
{
    private readonly JournalEntryErpNextSyncer _syncer;

    public JournalEntryPostedOutboxHandler(JournalEntryErpNextSyncer syncer) { _syncer = syncer; }

    public string EventType => OutboxEventTypes.JournalEntryPosted;

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<JournalEntryEventPayload>(message.PayloadJson);
        if (payload is not null) await _syncer.SyncAsync(payload.JournalEntryId, cancellationToken);
    }
}

/// <summary>A voided manual entry is cancelled in ERPNext.</summary>
public sealed class JournalEntryVoidedOutboxHandler : IOutboxEventHandler
{
    private readonly JournalEntryErpNextSyncer _syncer;

    public JournalEntryVoidedOutboxHandler(JournalEntryErpNextSyncer syncer) { _syncer = syncer; }

    public string EventType => OutboxEventTypes.JournalEntryVoided;

    public async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Deserialize<JournalEntryEventPayload>(message.PayloadJson);
        if (payload is not null) await _syncer.CancelAsync(payload.JournalEntryId, cancellationToken);
    }
}
