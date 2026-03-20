using AutoPartsERP.Application.Common.Abstractions.Repositories;

namespace AutoPartsERP.Infrastructure.Persistence.Repositories;

public sealed class WarrantyRepository : IWarrantyRepository
{
    private readonly AppDbContext _dbContext;

    public WarrantyRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(WarrantyRecord warranty, CancellationToken cancellationToken = default)
    {
        await _dbContext.WarrantyRecords.AddAsync(warranty, cancellationToken);
    }

    public async Task<WarrantyRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.WarrantyRecords.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public IQueryable<WarrantyRecord> Query() => _dbContext.WarrantyRecords.AsQueryable();
}
