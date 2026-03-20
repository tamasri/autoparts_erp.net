namespace AutoPartsERP.Application.Common.Abstractions.Repositories;

public interface ISkuRepository
{
    Task AddAsync(Sku sku, CancellationToken cancellationToken = default);
    Task<Sku?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Sku?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);
    IQueryable<Sku> Query();
}
