namespace AutoPartsERP.Infrastructure.Workers.OutboxDispatcher;

public sealed class OutboxDispatcherService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxDispatcherService> _logger;
    private readonly ErpMetrics _metrics;

    public OutboxDispatcherService(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxDispatcherService> logger,
        ErpMetrics metrics)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _metrics = metrics;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var outboxRepository = scope.ServiceProvider.GetRequiredService<IOutboxRepository>();
                var handlers = scope.ServiceProvider.GetServices<IOutboxEventHandler>().ToArray();

                var messages = await outboxRepository.GetUnprocessedAsync(50, stoppingToken);

                foreach (var message in messages)
                {
                    try
                    {
                        var handler = handlers.FirstOrDefault(x => string.Equals(x.EventType, message.EventType, StringComparison.Ordinal));
                        if (handler is null)
                        {
                            _logger.LogWarning(
                                "No outbox handler configured for event {EventType}. Marking as processed. MessageId={MessageId}",
                                message.EventType,
                                message.Id);
                            await outboxRepository.MarkProcessedAsync(message.Id, stoppingToken);
                            _metrics.RecordOutboxProcessed();
                            continue;
                        }

                        await handler.HandleAsync(message, stoppingToken);
                        await outboxRepository.MarkProcessedAsync(message.Id, stoppingToken);
                        _metrics.RecordOutboxProcessed();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(
                            ex,
                            "Outbox dispatch failed. MessageId={MessageId} EventType={EventType}",
                            message.Id,
                            message.EventType);

                        await outboxRepository.MarkFailedAsync(message.Id, ex.Message, stoppingToken);
                        _metrics.RecordOutboxFailed();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox dispatcher cycle failed.");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }
}
