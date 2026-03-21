namespace AutoPartsERP.Application.Common.Abstractions.Repositories;

public interface IStockAdjustmentRepository
{
    Task<StockAdjustment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(StockAdjustment adjustment, CancellationToken cancellationToken = default);
}

