using AutoPartsERP.Domain.Operational.Enums;

namespace AutoPartsERP.Domain.Operational;

public sealed class Location : AuditableEntity
{
    public Location(
        Guid id,
        string code,
        string name,
        LocationType type,
        Guid createdBy,
        Guid? parentId = null)
        : base(id)
    {
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        Type = type;
        ParentId = parentId;
        IsActive = true;
        CreatedBy = createdBy;
    }

    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public LocationType Type { get; private set; }

    public Guid? ParentId { get; private set; }

    public bool IsActive { get; private set; }

    public Guid CreatedBy { get; private set; }
}
