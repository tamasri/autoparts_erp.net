namespace AutoPartsERP.Contracts.Users;

public sealed record CreateUserRequest(
    string UserName,
    string Email,
    string FirstName,
    string LastName,
    string Password,
    IReadOnlyCollection<Guid> RoleIds);

/// <summary>The profile only. Roles change through AssignUserRolesRequest and (de)activation through their own commands, both governed.</summary>
public sealed record UpdateUserRequest(
    string Email,
    string FirstName,
    string LastName);

public sealed record ResetUserPasswordRequest(string NewPassword);

public sealed record SetUserLockRequest(DateTimeOffset? LockoutEndUtc);

public sealed record AssignUserRolesRequest(IReadOnlyCollection<Guid> RoleIds);
