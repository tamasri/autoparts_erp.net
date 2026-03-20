namespace AutoPartsERP.Application.Common.Abstractions.Repositories;

public interface IInventoryRepository
{
    Task<InventoryStock?> GetBySkuAndLocationAsync(Guid skuId, Guid locationId, CancellationToken cancellationToken = default);
    Task<InventoryStock?> GetWithLockAsync(Guid skuId, Guid locationId, CancellationToken cancellationToken = default);
    Task AddAsync(InventoryStock stock, CancellationToken cancellationToken = default);
}
