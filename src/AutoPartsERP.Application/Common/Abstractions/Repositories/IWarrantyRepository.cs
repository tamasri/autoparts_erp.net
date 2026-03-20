namespace AutoPartsERP.Application.Common.Abstractions.Repositories;

public interface IWarrantyRepository
{
    Task AddAsync(WarrantyRecord warranty, CancellationToken cancellationToken = default);
    Task<WarrantyRecord?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    IQueryable<WarrantyRecord> Query();
}
