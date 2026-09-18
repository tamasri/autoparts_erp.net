namespace AutoPartsERP.Infrastructure.Jobs;

/// <summary>
/// Keeps inventory_balances (read by every WMS screen: Receiving, Transfers, Cycle Counts, Stock
/// Adjustments, Issue Orders, Inventory Alerts) caught up with inventory_stock (the authoritative
/// table for sales/invoicing). See AddInventoryBalancesSyncFunction for why this is a periodic
/// reconciliation rather than a live view/trigger.
/// </summary>
public sealed class SyncInventoryBalancesJob
{
    private readonly IDbConnectionFactory _connectionFactory;

    public SyncInventoryBalancesJob(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    [Queue("governance")]
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _connectionFactory.CreateAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            "SELECT sync_inventory_balances_from_stock();",
            cancellationToken: cancellationToken));
    }
}
