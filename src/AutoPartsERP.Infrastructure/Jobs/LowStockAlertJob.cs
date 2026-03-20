namespace AutoPartsERP.Infrastructure.Jobs;

public sealed class LowStockAlertJob
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<LowStockAlertJob> _logger;

    public LowStockAlertJob(IDbConnectionFactory connectionFactory, ILogger<LowStockAlertJob> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    [Queue("governance")]
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        var lowStockCount = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM sku_stock_summary WHERE low_stock_flag = true;",
            cancellationToken: cancellationToken));

        if (lowStockCount > 0)
        {
            _logger.LogWarning("Low stock alert job found {Count} low-stock SKUs.", lowStockCount);
        }
    }
}
