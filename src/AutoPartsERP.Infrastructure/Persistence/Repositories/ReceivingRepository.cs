using AutoPartsERP.Application.Common.Abstractions.Repositories;

namespace AutoPartsERP.Infrastructure.Persistence.Repositories;

public sealed class ReceivingRepository : IReceivingRepository
{
    private readonly AppDbContext _dbContext;

    public ReceivingRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<ReceivingDocument?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ReceivingDocuments
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task AddAsync(ReceivingDocument document, CancellationToken cancellationToken = default)
    {
        await _dbContext.ReceivingDocuments.AddAsync(document, cancellationToken);
    }
}

