using AutoPartsERP.Application.Common.Abstractions.Repositories;

namespace AutoPartsERP.Infrastructure.Persistence.Repositories;

public sealed class TransferOrderRepository : ITransferOrderRepository
{
    private readonly AppDbContext _dbContext;

    public TransferOrderRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TransferOrder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.TransferOrders
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task AddAsync(TransferOrder transferOrder, CancellationToken cancellationToken = default)
    {
        await _dbContext.TransferOrders.AddAsync(transferOrder, cancellationToken);
    }
}

