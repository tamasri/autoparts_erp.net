using AutoPartsERP.Domain.Common;

namespace AutoPartsERP.Domain.Identity;

public sealed class AppRole : AuditableEntity
{
    private readonly HashSet<string> _permissions;

    public AppRole(
        Guid id,
        string code,
        string name,
        string description,
        bool isSystem,
        IEnumerable<string>? permissions = null)
        : base(id)
    {
        Code = Normalize(code);
        Name = name.Trim();
        Description = description.Trim();
        IsSystem = isSystem;
        IsActive = true;
        _permissions = new HashSet<string>(permissions ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
    }

    public string Code { get; private set; }

    public string Name { get; private set; }

    public string Description { get; private set; }

    public bool IsSystem { get; }

    public bool IsActive { get; private set; }

    public IReadOnlyCollection<string> Permissions => _permissions.ToArray();

    public void UpdateDetails(string name, string description)
    {
        Name = name.Trim();
        Description = description.Trim();
        Touch();
    }

    public void Activate()
    {
        IsActive = true;
        Touch();
    }

    public void Deactivate()
    {
        IsActive = false;
        Touch();
    }

    public void AddPermission(string permissionCode)
    {
        if (_permissions.Add(permissionCode.Trim()))
        {
            Touch();
        }
    }

    public void RemovePermission(string permissionCode)
    {
        if (_permissions.Remove(permissionCode.Trim()))
        {
            Touch();
        }
    }

    public void ReplacePermissions(IEnumerable<string> permissionCodes)
    {
        _permissions.Clear();

        foreach (var permissionCode in permissionCodes)
        {
            _permissions.Add(permissionCode.Trim());
        }

        Touch();
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToUpperInvariant();
    }
}
