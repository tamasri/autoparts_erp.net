using AutoPartsERP.Application.Common.Abstractions.Repositories;
using Microsoft.EntityFrameworkCore.Storage;

namespace AutoPartsERP.Infrastructure.Persistence.Repositories;

public sealed class InventoryRepository : IInventoryRepository
{
    private readonly AppDbContext _dbContext;

    public InventoryRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<InventoryStock?> GetBySkuAndLocationAsync(Guid skuId, Guid locationId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.InventoryStocks
            .FirstOrDefaultAsync(x => x.SkuId == skuId && x.LocationId == locationId, cancellationToken);
    }

    public async Task<InventoryStock?> GetWithLockAsync(Guid skuId, Guid locationId, CancellationToken cancellationToken = default)
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
        await connection.ExecuteAsync(
            new CommandDefinition(
                "SELECT id FROM inventory_stock WHERE sku_id = @SkuId AND location_id = @LocationId FOR UPDATE;",
                new { SkuId = skuId, LocationId = locationId },
                transaction,
                cancellationToken: cancellationToken));

        return await _dbContext.InventoryStocks
            .FirstOrDefaultAsync(x => x.SkuId == skuId && x.LocationId == locationId, cancellationToken);
    }

    public async Task AddAsync(InventoryStock stock, CancellationToken cancellationToken = default)
    {
        await _dbContext.InventoryStocks.AddAsync(stock, cancellationToken);
    }
}
