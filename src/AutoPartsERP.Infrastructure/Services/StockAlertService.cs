using AutoPartsERP.Application.Features.InventoryAlerts;

namespace AutoPartsERP.Infrastructure.Services;

/// <summary>Runs <see cref="StockAlertScanner"/> and tells the right people about what it raised. Never throws (alerts are a side effect).</summary>
public sealed class StockAlertService
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly IRealtimeNotifier _notifier;
    private readonly ILogger<StockAlertService> _logger;

    public StockAlertService(IDbConnectionFactory connectionFactory, IRealtimeNotifier notifier, ILogger<StockAlertService> logger)
    {
        _connectionFactory = connectionFactory;
        _notifier = notifier;
        _logger = logger;
    }

    /// <summary>The items on one invoice (right after it was posted or voided).</summary>
    public async Task ScanInvoiceAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
            var skus = (await connection.QueryAsync<Guid>(new CommandDefinition(
                "SELECT DISTINCT sku_id FROM invoice_lines WHERE invoice_id = @invoiceId;", new { invoiceId }, cancellationToken: cancellationToken))).ToList();
            if (skus.Count > 0)
            {
                await StockAlertScanner.NotifyAsync(_notifier, await StockAlertScanner.ScanAsync(connection, skus, cancellationToken), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Stock alert scan after invoice {InvoiceId} failed.", invoiceId);
        }
    }

    /// <summary>Every item (the periodic safety net for transfers, adjustments, counts, receipts).</summary>
    public async Task<int> ScanAllAsync(CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var raised = await StockAlertScanner.ScanAsync(connection, null, cancellationToken);
        await StockAlertScanner.NotifyAsync(_notifier, raised, cancellationToken);
        return raised.Count;
    }
}
