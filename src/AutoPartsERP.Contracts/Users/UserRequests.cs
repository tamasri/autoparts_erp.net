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

/// <summary>One warehouse a user works in; <c>IsManager</c> lets them approve transfers into or out of it.</summary>
public sealed record UserWarehouseInput(Guid WarehouseId, bool IsManager);

public sealed record SetUserWarehousesRequest(IReadOnlyList<UserWarehouseInput> Warehouses);

/// <summary>Every warehouse, with whether the user works in it and manages it.</summary>
public sealed record UserWarehouseDto(Guid WarehouseId, string Code, string Name, string Type, bool Assigned, bool IsManager);
