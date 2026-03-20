namespace AutoPartsERP.Application.Common.Abstractions.Repositories;

public interface IPaymentRepository
{
    Task AddAsync(Payment payment, CancellationToken cancellationToken = default);
    Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    IQueryable<Payment> Query();
}
