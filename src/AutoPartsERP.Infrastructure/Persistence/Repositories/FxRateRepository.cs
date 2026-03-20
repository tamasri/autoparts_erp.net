using AutoPartsERP.Application.Common.Abstractions.Repositories;

namespace AutoPartsERP.Infrastructure.Persistence.Repositories;

public sealed class FxRateRepository : IFxRateRepository
{
    private readonly AppDbContext _dbContext;

    public FxRateRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(FxRate fxRate, CancellationToken cancellationToken = default)
    {
        await _dbContext.FxRates.AddAsync(fxRate, cancellationToken);
    }

    public async Task<FxRate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.FxRates.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<FxRate?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.FxRates
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.RateDate)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public IQueryable<FxRate> Query() => _dbContext.FxRates.AsQueryable();
}
