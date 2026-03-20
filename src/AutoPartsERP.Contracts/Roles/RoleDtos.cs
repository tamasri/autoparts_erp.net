namespace AutoPartsERP.Contracts.Roles;

public sealed record RoleSummaryDto(
    Guid Id,
    string Code,
    string Name,
    string Description,
    bool IsSystem,
    bool IsActive,
    IReadOnlyCollection<string> Permissions,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? UpdatedAtUtc);
