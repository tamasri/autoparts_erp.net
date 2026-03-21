namespace AutoPartsERP.Application.Common.Abstractions.Repositories;

public interface ITransferOrderRepository
{
    Task<TransferOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(TransferOrder transferOrder, CancellationToken cancellationToken = default);
}

