namespace AutoPartsERP.Contracts.Roles;

public sealed record CreateRoleRequest(
    string Code,
    string Name,
    string Description,
    IReadOnlyCollection<string> Permissions,
    bool IsSystem = false);

public sealed record UpdateRoleRequest(
    string Name,
    string Description,
    bool IsActive,
    IReadOnlyCollection<string> Permissions);
