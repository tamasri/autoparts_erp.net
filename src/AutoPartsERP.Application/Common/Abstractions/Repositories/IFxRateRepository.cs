namespace AutoPartsERP.Application.Common.Abstractions.Repositories;

public interface IFxRateRepository
{
    Task AddAsync(FxRate fxRate, CancellationToken cancellationToken = default);
    Task<FxRate?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<FxRate?> GetLatestAsync(CancellationToken cancellationToken = default);
    IQueryable<FxRate> Query();
}
