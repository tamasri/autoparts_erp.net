using AutoPartsERP.Application.Common.Abstractions.Repositories;

namespace AutoPartsERP.Infrastructure.Persistence.Repositories;

public sealed class StockAdjustmentRepository : IStockAdjustmentRepository
{
    private readonly AppDbContext _dbContext;

    public StockAdjustmentRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<StockAdjustment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.StockAdjustments
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task AddAsync(StockAdjustment adjustment, CancellationToken cancellationToken = default)
    {
        await _dbContext.StockAdjustments.AddAsync(adjustment, cancellationToken);
    }
}

