namespace AutoPartsERP.Application.Common.Abstractions.Repositories;

public interface ICategoryRepository
{
    Task AddAsync(Category category, CancellationToken cancellationToken = default);
    Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Category?> GetByPathAsync(string path, CancellationToken cancellationToken = default);
    IQueryable<Category> Query();
}
