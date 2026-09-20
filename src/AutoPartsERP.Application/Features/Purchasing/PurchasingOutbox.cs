using System.Data.Common;
using Dapper;

namespace AutoPartsERP.Application.Features.Purchasing;

/// <summary>Writes an outbox row in the caller's transaction, so the ERPNext hand-off is committed together with the change it reports.</summary>
internal static class PurchasingOutbox
{
    public static Task AddAsync<TPayload>(
        DbConnection connection, DbTransaction transaction, string eventType, string aggregateType, Guid aggregateId,
        TPayload payload, Guid correlationId, CancellationToken cancellationToken)
        where TPayload : notnull
    {
        var message = OutboxMessage.Create(eventType, aggregateType, aggregateId, payload, correlationId);
        return connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO outbox_messages (
                id, event_type, aggregate_type, aggregate_id, payload_json, occurred_at,
                processed_at, processing_error, retry_count, correlation_id)
            VALUES (
                @Id, @EventType, @AggregateType, @AggregateId, @PayloadJson, @OccurredAt,
                @ProcessedAt, @ProcessingError, @RetryCount, @CorrelationId);
            """,
            new
            {
                message.Id, message.EventType, message.AggregateType, message.AggregateId, message.PayloadJson, message.OccurredAt,
                message.ProcessedAt, message.ProcessingError, message.RetryCount, message.CorrelationId
            },
            transaction,
            cancellationToken: cancellationToken));
    }
}
