using System.Text.Json;

namespace AutoPartsERP.Domain.Messaging;

public sealed class OutboxMessage
{
    private OutboxMessage() { }

    public Guid Id { get; private set; } = Guid.NewGuid();

    public string EventType { get; private set; } = string.Empty;

    public string AggregateType { get; private set; } = string.Empty;

    public Guid AggregateId { get; private set; }

    public string PayloadJson { get; private set; } = string.Empty;

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset? ProcessedAt { get; private set; }

    public string? ProcessingError { get; private set; }

    public int RetryCount { get; private set; }

    public Guid CorrelationId { get; private set; }

    public static OutboxMessage Create<T>(
        string eventType,
        string aggregateType,
        Guid aggregateId,
        T payload,
        Guid correlationId)
        where T : notnull
    {
        return new OutboxMessage
        {
            Id = Guid.NewGuid(),
            EventType = eventType.Trim(),
            AggregateType = aggregateType.Trim(),
            AggregateId = aggregateId,
            PayloadJson = JsonSerializer.Serialize(payload),
            OccurredAt = DateTimeOffset.UtcNow,
            CorrelationId = correlationId
        };
    }

    public static OutboxMessage FromStorage(
        Guid id,
        string eventType,
        string aggregateType,
        Guid aggregateId,
        string payloadJson,
        DateTimeOffset occurredAt,
        DateTimeOffset? processedAt,
        string? processingError,
        int retryCount,
        Guid correlationId)
    {
        return new OutboxMessage
        {
            Id = id,
            EventType = eventType,
            AggregateType = aggregateType,
            AggregateId = aggregateId,
            PayloadJson = payloadJson,
            OccurredAt = occurredAt,
            ProcessedAt = processedAt,
            ProcessingError = processingError,
            RetryCount = retryCount,
            CorrelationId = correlationId
        };
    }

    public void MarkProcessed()
    {
        ProcessedAt = DateTimeOffset.UtcNow;
        ProcessingError = null;
    }

    public void MarkFailed(string error)
    {
        ProcessingError = error;
        RetryCount++;
    }
}
