using AutoPartsERP.Application.Common.Abstractions.Repositories;

namespace AutoPartsERP.Infrastructure.Persistence.Repositories;

public sealed class ItemRepository : IItemRepository
{
    private readonly AppDbContext _dbContext;
    private readonly IPartNumberService _partNumberService;

    public ItemRepository(AppDbContext dbContext, IPartNumberService partNumberService)
    {
        _dbContext = dbContext;
        _partNumberService = partNumberService;
    }

    public async Task<Item?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Items
            .Include(x => x.Aliases)
            .Include(x => x.Interchanges)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<Item?> GetByPartNumberCanonicalAsync(string canonicalPartNumber, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(canonicalPartNumber))
        {
            return null;
        }

        var normalized = _partNumberService.NormalizePartNumber(canonicalPartNumber).Canonical;
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return await _dbContext.Items.FirstOrDefaultAsync(
            x => x.PartNumberCanonical == normalized,
            cancellationToken);
    }

    public async Task<Item?> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return null;
        }

        var normalized = barcode.Trim();
        return await _dbContext.Items
            .Join(
                _dbContext.Skus.Where(s => s.Barcode != null),
                item => item.SkuId,
                sku => sku.Id,
                (item, sku) => new { Item = item, sku.Barcode })
            .Where(x => x.Barcode == normalized)
            .Select(x => x.Item)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task AddAsync(Item item, CancellationToken cancellationToken = default)
    {
        await _dbContext.Items.AddAsync(item, cancellationToken);
    }
}
