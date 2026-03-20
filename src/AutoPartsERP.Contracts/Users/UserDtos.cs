namespace AutoPartsERP.Contracts.Users;

public sealed record UserRoleDto(Guid RoleId, string Code, string Name);

public sealed record UserSummaryDto(
    Guid Id,
    string UserName,
    string Email,
    string FirstName,
    string LastName,
    bool IsActive,
    bool IsLockedOut,
    IReadOnlyCollection<UserRoleDto> Roles,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastLoginAtUtc);

public sealed record UserDetailsDto(
    Guid Id,
    string UserName,
    string Email,
    string FirstName,
    string LastName,
    bool IsActive,
    bool IsLockedOut,
    IReadOnlyCollection<UserRoleDto> Roles,
    IReadOnlyCollection<string> Permissions,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastLoginAtUtc,
    DateTimeOffset? UpdatedAtUtc);
