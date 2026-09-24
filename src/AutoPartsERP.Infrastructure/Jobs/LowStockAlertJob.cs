using AutoPartsERP.Infrastructure.Services;

namespace AutoPartsERP.Infrastructure.Jobs;

public sealed class LowStockAlertJob
{
    private readonly StockAlertService _stockAlerts;
    private readonly ILogger<LowStockAlertJob> _logger;

    public LowStockAlertJob(StockAlertService stockAlerts, ILogger<LowStockAlertJob> logger)
    {
        _stockAlerts = stockAlerts;
        _logger = logger;
    }

    /// <summary>
    /// Every 10 minutes: raises and resolves stock alerts for all items. Sales are scanned at once (invoice outbox handlers);
    /// this catches everything else — transfers, adjustments, counts, receipts, reorder levels changed on the item card.
    /// (Before 2026-09-24 this job only logged a count and inventory_alerts stayed empty.)
    /// </summary>
    [Queue("governance")]
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var raised = await _stockAlerts.ScanAllAsync(cancellationToken);
        if (raised > 0)
        {
            _logger.LogInformation("Stock alert scan raised {Count} alert(s).", raised);
        }
    }
}
