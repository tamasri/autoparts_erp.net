using AutoPartsERP.Application.Common.Abstractions.Repositories;
using Microsoft.EntityFrameworkCore.Storage;

namespace AutoPartsERP.Infrastructure.Persistence.Repositories;

public sealed class InventoryBalanceRepository : IInventoryBalanceRepository
{
    private readonly AppDbContext _dbContext;

    public InventoryBalanceRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<InventoryBalance?> GetByKeyAsync(
        Guid itemId,
        Guid locationId,
        Guid? batchId,
        string status,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.InventoryBalances.FirstOrDefaultAsync(
            x => x.ItemId == itemId &&
                 x.LocationId == locationId &&
                 x.BatchId == batchId &&
                 x.Status == status,
            cancellationToken);
    }

    public async Task<InventoryBalance?> GetByKeyForUpdateAsync(
        Guid itemId,
        Guid locationId,
        Guid? batchId,
        string status,
        CancellationToken cancellationToken = default)
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
        await connection.ExecuteAsync(
            new CommandDefinition(
                """
                SELECT id
                FROM inventory_balances
                WHERE item_id = @ItemId
                  AND location_id = @LocationId
                  AND ((@BatchId IS NULL AND batch_id IS NULL) OR batch_id = @BatchId)
                  AND status = @Status
                FOR UPDATE;
                """,
                new { ItemId = itemId, LocationId = locationId, BatchId = batchId, Status = status },
                transaction,
                cancellationToken: cancellationToken));

        return await GetByKeyAsync(itemId, locationId, batchId, status, cancellationToken);
    }

    public async Task AddAsync(InventoryBalance balance, CancellationToken cancellationToken = default)
    {
        await _dbContext.InventoryBalances.AddAsync(balance, cancellationToken);
    }
}

