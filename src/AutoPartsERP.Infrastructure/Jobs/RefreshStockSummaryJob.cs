namespace AutoPartsERP.Infrastructure.Jobs;

public sealed class RefreshStockSummaryJob
{
    private readonly IDbConnectionFactory _connectionFactory;

    public RefreshStockSummaryJob(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    [Queue("governance")]
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "REFRESH MATERIALIZED VIEW CONCURRENTLY sku_stock_summary;",
            cancellationToken: cancellationToken));
    }
}
