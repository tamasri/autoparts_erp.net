using AutoPartsERP.Application.Common.Abstractions.Repositories;

namespace AutoPartsERP.Infrastructure.Persistence.Repositories;

public sealed class PaymentRepository : IPaymentRepository
{
    private readonly AppDbContext _dbContext;

    public PaymentRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(Payment payment, CancellationToken cancellationToken = default)
    {
        await _dbContext.Payments.AddAsync(payment, cancellationToken);
    }

    public async Task<Payment?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Payments.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public IQueryable<Payment> Query() => _dbContext.Payments.AsQueryable();
}
