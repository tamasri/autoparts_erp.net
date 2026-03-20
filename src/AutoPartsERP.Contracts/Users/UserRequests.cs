namespace AutoPartsERP.Contracts.Users;

public sealed record CreateUserRequest(
    string UserName,
    string Email,
    string FirstName,
    string LastName,
    string Password,
    IReadOnlyCollection<Guid> RoleIds);

public sealed record UpdateUserRequest(
    string Email,
    string FirstName,
    string LastName,
    bool IsActive,
    IReadOnlyCollection<Guid> RoleIds);

public sealed record SetUserLockRequest(DateTimeOffset? LockoutEndUtc);

public sealed record AssignUserRolesRequest(IReadOnlyCollection<Guid> RoleIds);
