namespace AutoPartsERP.Application.Common.Abstractions.Repositories;

public interface IInventoryBalanceRepository
{
    Task<InventoryBalance?> GetByKeyAsync(
        Guid itemId,
        Guid locationId,
        Guid? batchId,
        string status,
        CancellationToken cancellationToken = default);

    Task<InventoryBalance?> GetByKeyForUpdateAsync(
        Guid itemId,
        Guid locationId,
        Guid? batchId,
        string status,
        CancellationToken cancellationToken = default);

    Task AddAsync(InventoryBalance balance, CancellationToken cancellationToken = default);
}

