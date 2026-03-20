using AutoPartsERP.Application.Common.Abstractions.Repositories;

namespace AutoPartsERP.Infrastructure.Persistence.Repositories;

public sealed class CustomerRepository : ICustomerRepository
{
    private readonly AppDbContext _dbContext;

    public CustomerRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        await _dbContext.Customers.AddAsync(customer, cancellationToken);
    }

    public async Task<Customer?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Customers.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<Customer?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return await _dbContext.Customers.FirstOrDefaultAsync(x => x.Code == normalized, cancellationToken);
    }

    public IQueryable<Customer> Query() => _dbContext.Customers.AsQueryable();
}
