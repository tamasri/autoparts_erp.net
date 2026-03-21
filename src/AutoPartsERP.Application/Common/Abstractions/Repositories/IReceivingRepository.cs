namespace AutoPartsERP.Application.Common.Abstractions.Repositories;

public interface IReceivingRepository
{
    Task<ReceivingDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(ReceivingDocument document, CancellationToken cancellationToken = default);
}

