namespace AutoPartsERP.Application.Common.Abstractions.Repositories;

public interface IInvoiceRepository
{
    Task AddAsync(Invoice invoice, CancellationToken cancellationToken = default);
    Task<Invoice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Invoice?> GetWithLockAsync(Guid id, CancellationToken cancellationToken = default);
    IQueryable<Invoice> Query();
}
