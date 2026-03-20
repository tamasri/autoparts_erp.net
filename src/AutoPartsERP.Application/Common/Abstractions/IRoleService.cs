namespace AutoPartsERP.Application.Common.Abstractions;

public interface IRoleService
{
    Task<Result<IReadOnlyCollection<RoleSummaryDto>>> GetRolesAsync(CancellationToken cancellationToken = default);

    Task<Result<RoleSummaryDto>> GetRoleByIdAsync(Guid roleId, CancellationToken cancellationToken = default);

    Task<Result<RoleSummaryDto>> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken = default);

    Task<Result<RoleSummaryDto>> UpdateRoleAsync(Guid roleId, UpdateRoleRequest request, CancellationToken cancellationToken = default);

    Task<Result<RoleSummaryDto>> GrantPermissionAsync(Guid roleId, string permissionCode, CancellationToken cancellationToken = default);

    Task<Result<RoleSummaryDto>> RevokePermissionAsync(Guid roleId, string permissionCode, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyCollection<string>>> GetAllPermissionsAsync(CancellationToken cancellationToken = default);
}
