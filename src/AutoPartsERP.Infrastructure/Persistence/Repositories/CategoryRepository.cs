using AutoPartsERP.Application.Common.Abstractions.Repositories;

namespace AutoPartsERP.Infrastructure.Persistence.Repositories;

public sealed class CategoryRepository : ICategoryRepository
{
    private readonly AppDbContext _dbContext;

    public CategoryRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(Category category, CancellationToken cancellationToken = default)
    {
        await _dbContext.Categories.AddAsync(category, cancellationToken);
    }

    public async Task<Category?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Categories.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<Category?> GetByPathAsync(string path, CancellationToken cancellationToken = default)
    {
        var normalized = path.Trim().ToLowerInvariant();
        return await _dbContext.Categories.FirstOrDefaultAsync(x => x.Path.Value == normalized, cancellationToken);
    }

    public IQueryable<Category> Query() => _dbContext.Categories.AsQueryable();
}
