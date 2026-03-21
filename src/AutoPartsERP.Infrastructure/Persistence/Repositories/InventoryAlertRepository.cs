using AutoPartsERP.Application.Common.Abstractions.Repositories;

namespace AutoPartsERP.Infrastructure.Persistence.Repositories;

public sealed class InventoryAlertRepository : IInventoryAlertRepository
{
    private readonly AppDbContext _dbContext;

    public InventoryAlertRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyCollection<InventoryAlert>> GetOpenAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.InventoryAlerts
            .Where(x => x.Status != "RESOLVED")
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<InventoryAlert?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.InventoryAlerts.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }
}

