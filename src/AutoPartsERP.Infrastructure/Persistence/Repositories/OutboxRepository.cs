using AutoPartsERP.Application.Common.Abstractions.Repositories;

namespace AutoPartsERP.Infrastructure.Persistence.Repositories;

public sealed class OutboxRepository : IOutboxRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public OutboxRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<IReadOnlyCollection<OutboxMessage>> GetUnprocessedAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var rows = await connection.QueryAsync<OutboxRow>(new CommandDefinition(
            """
            SELECT
                id AS Id,
                event_type AS EventType,
                aggregate_type AS AggregateType,
                aggregate_id AS AggregateId,
                payload_json AS PayloadJson,
                occurred_at AS OccurredAt,
                processed_at AS ProcessedAt,
                processing_error AS ProcessingError,
                retry_count AS RetryCount,
                correlation_id AS CorrelationId
            FROM outbox_messages
            WHERE processed_at IS NULL
              AND retry_count < 5
            ORDER BY occurred_at
            LIMIT @BatchSize
            FOR UPDATE SKIP LOCKED;
            """,
            new { BatchSize = batchSize <= 0 ? 50 : batchSize },
            transaction,
            cancellationToken: cancellationToken));

        var messages = rows
            .Select(row => OutboxMessage.FromStorage(
                row.Id,
                row.EventType,
                row.AggregateType,
                row.AggregateId,
                row.PayloadJson,
                row.OccurredAt,
                row.ProcessedAt,
                row.ProcessingError,
                row.RetryCount,
                row.CorrelationId))
            .ToArray();

        await transaction.CommitAsync(cancellationToken);
        return messages;
    }

    public async Task MarkProcessedAsync(Guid messageId, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE outbox_messages
            SET processed_at = now(),
                processing_error = NULL
            WHERE id = @MessageId
              AND processed_at IS NULL;
            """,
            new { MessageId = messageId },
            cancellationToken: cancellationToken));
    }

    public async Task MarkFailedAsync(Guid messageId, string error, CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            UPDATE outbox_messages
            SET processing_error = @Error,
                retry_count = retry_count + 1
            WHERE id = @MessageId
              AND processed_at IS NULL;
            """,
            new { MessageId = messageId, Error = error },
            cancellationToken: cancellationToken));
    }

    private sealed class OutboxRow
    {
        public Guid Id { get; init; }
        public string EventType { get; init; } = string.Empty;
        public string AggregateType { get; init; } = string.Empty;
        public Guid AggregateId { get; init; }
        public string PayloadJson { get; init; } = string.Empty;
        public DateTimeOffset OccurredAt { get; init; }
        public DateTimeOffset? ProcessedAt { get; init; }
        public string? ProcessingError { get; init; }
        public int RetryCount { get; init; }
        public Guid CorrelationId { get; init; }
    }
}
