using AutoPartsERP.Application.Common.Abstractions.Repositories;

namespace AutoPartsERP.Infrastructure.Persistence.Repositories;

public sealed class SkuRepository : ISkuRepository
{
    private readonly AppDbContext _dbContext;

    public SkuRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(Sku sku, CancellationToken cancellationToken = default)
    {
        await _dbContext.Skus.AddAsync(sku, cancellationToken);
    }

    public async Task<Sku?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Skus.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<Sku?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return await _dbContext.Skus.FirstOrDefaultAsync(x => x.Code == normalized, cancellationToken);
    }

    public IQueryable<Sku> Query() => _dbContext.Skus.AsQueryable();
}
