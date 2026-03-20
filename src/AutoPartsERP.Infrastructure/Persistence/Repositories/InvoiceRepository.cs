using AutoPartsERP.Application.Common.Abstractions.Repositories;
using Microsoft.EntityFrameworkCore.Storage;

namespace AutoPartsERP.Infrastructure.Persistence.Repositories;

public sealed class InvoiceRepository : IInvoiceRepository
{
    private readonly AppDbContext _dbContext;

    public InvoiceRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(Invoice invoice, CancellationToken cancellationToken = default)
    {
        await _dbContext.Invoices.AddAsync(invoice, cancellationToken);
    }

    public async Task<Invoice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Invoices
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<Invoice?> GetWithLockAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var connection = _dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        var transaction = _dbContext.Database.CurrentTransaction?.GetDbTransaction();
        await connection.ExecuteAsync(
            new CommandDefinition(
                "SELECT id FROM invoices WHERE id = @Id FOR UPDATE;",
                new { Id = id },
                transaction,
                cancellationToken: cancellationToken));

        return await _dbContext.Invoices
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public IQueryable<Invoice> Query() => _dbContext.Invoices.AsQueryable();
}
