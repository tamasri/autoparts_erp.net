namespace AutoPartsERP.Application.Common.Abstractions.Repositories;

public interface IItemRepository
{
    Task<Item?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Item?> GetByPartNumberCanonicalAsync(string canonicalPartNumber, CancellationToken cancellationToken = default);
    Task<Item?> GetByBarcodeAsync(string barcode, CancellationToken cancellationToken = default);
    Task AddAsync(Item item, CancellationToken cancellationToken = default);
}

