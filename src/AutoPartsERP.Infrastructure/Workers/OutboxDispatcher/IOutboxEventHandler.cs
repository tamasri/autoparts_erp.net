namespace AutoPartsERP.Infrastructure.Workers.OutboxDispatcher;

public interface IOutboxEventHandler
{
    string EventType { get; }

    Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken);
}
