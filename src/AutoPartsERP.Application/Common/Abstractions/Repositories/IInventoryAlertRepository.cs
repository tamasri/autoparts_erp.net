namespace AutoPartsERP.Application.Common.Abstractions.Repositories;

public interface IInventoryAlertRepository
{
    Task<IReadOnlyCollection<InventoryAlert>> GetOpenAsync(CancellationToken cancellationToken = default);
    Task<InventoryAlert?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
}

