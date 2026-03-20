using AutoPartsERP.Domain.Operational.ValueObjects;

namespace AutoPartsERP.Domain.Operational;

public sealed class Category : AuditableEntity
{
    private Category(
        Guid id,
        CategoryPath path,
        string name,
        string? nameAr,
        Guid? parentId,
        int depth,
        Guid createdBy)
        : base(id)
    {
        Path = path;
        Name = name;
        NameAr = nameAr;
        ParentId = parentId;
        Depth = depth;
        IsActive = true;
        CreatedBy = createdBy;
    }

    public CategoryPath Path { get; private set; } = new("root");

    public string Name { get; private set; } = string.Empty;

    public string? NameAr { get; private set; }

    public Guid? ParentId { get; private set; }

    public int Depth { get; private set; }

    public bool IsActive { get; private set; }

    public Guid CreatedBy { get; private set; }

    public static Result<Category> Create(
        string path,
        string name,
        string? nameAr,
        Guid createdBy,
        Guid? parentId = null,
        int depth = 0)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return Result<Category>.Failure(new Error("Category.PathRequired", "Category path is required."));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return Result<Category>.Failure(new Error("Category.NameRequired", "Category name is required."));
        }

        return Result<Category>.Success(new Category(
            Guid.NewGuid(),
            new CategoryPath(path),
            name.Trim(),
            nameAr?.Trim(),
            parentId,
            depth,
            createdBy));
    }

    public bool IsDescendantOf(string parentPath) => Path.IsDescendantOf(parentPath);
}
