using AutoPartsERP.Infrastructure.Identity;

namespace AutoPartsERP.Infrastructure.Services;

public sealed class RoleService : IRoleService
{
    private readonly RoleManager<AppRole> _roleManager;

    public RoleService(RoleManager<AppRole> roleManager)
    {
        _roleManager = roleManager;
    }

    public async Task<Result<IReadOnlyCollection<RoleSummaryDto>>> GetRolesAsync(CancellationToken cancellationToken = default)
    {
        var roles = await _roleManager.Roles.OrderBy(x => x.Name).ToListAsync(cancellationToken);
        var items = new List<RoleSummaryDto>(roles.Count);
        foreach (var role in roles)
        {
            items.Add(await ToDto(role));
        }

        return Result<IReadOnlyCollection<RoleSummaryDto>>.Success(items);
    }

    public async Task<Result<RoleSummaryDto>> GetRoleByIdAsync(Guid roleId, CancellationToken cancellationToken = default)
    {
        var role = await _roleManager.FindByIdAsync(roleId.ToString());
        if (role is null)
        {
            return Result<RoleSummaryDto>.Failure(new Error("Roles.NotFound", "Role was not found."));
        }

        return Result<RoleSummaryDto>.Success(await ToDto(role));
    }

    public async Task<Result<RoleSummaryDto>> CreateRoleAsync(CreateRoleRequest request, CancellationToken cancellationToken = default)
    {
        var role = new AppRole
        {
            Id = Guid.NewGuid(),
            Name = request.Code.ToUpperInvariant(),
            NormalizedName = request.Code.ToUpperInvariant(),
            Description = request.Description,
            IsSystem = request.IsSystem,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = Guid.Empty
        };

        var created = await _roleManager.CreateAsync(role);
        if (!created.Succeeded)
        {
            return Result<RoleSummaryDto>.Failure(new Error("Roles.CreateFailed", string.Join("; ", created.Errors.Select(x => x.Description))));
        }

        foreach (var permission in request.Permissions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await _roleManager.AddClaimAsync(role, new Claim("permission", permission));
        }

        return Result<RoleSummaryDto>.Success(await ToDto(role));
    }

    public async Task<Result<RoleSummaryDto>> UpdateRoleAsync(Guid roleId, UpdateRoleRequest request, CancellationToken cancellationToken = default)
    {
        var role = await _roleManager.FindByIdAsync(roleId.ToString());
        if (role is null)
        {
            return Result<RoleSummaryDto>.Failure(new Error("Roles.NotFound", "Role was not found."));
        }

        role.Description = request.Description;
        var updated = await _roleManager.UpdateAsync(role);
        if (!updated.Succeeded)
        {
            return Result<RoleSummaryDto>.Failure(new Error("Roles.UpdateFailed", string.Join("; ", updated.Errors.Select(x => x.Description))));
        }

        var existingClaims = await _roleManager.GetClaimsAsync(role);
        foreach (var claim in existingClaims.Where(x => x.Type == "permission").ToArray())
        {
            await _roleManager.RemoveClaimAsync(role, claim);
        }

        foreach (var permission in request.Permissions.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await _roleManager.AddClaimAsync(role, new Claim("permission", permission));
        }

        return Result<RoleSummaryDto>.Success(await ToDto(role));
    }

    public async Task<Result<RoleSummaryDto>> GrantPermissionAsync(Guid roleId, string permissionCode, CancellationToken cancellationToken = default)
    {
        var role = await _roleManager.FindByIdAsync(roleId.ToString());
        if (role is null)
        {
            return Result<RoleSummaryDto>.Failure(new Error("Roles.NotFound", "Role was not found."));
        }

        await _roleManager.AddClaimAsync(role, new Claim("permission", permissionCode));
        return Result<RoleSummaryDto>.Success(await ToDto(role));
    }

    public async Task<Result<RoleSummaryDto>> RevokePermissionAsync(Guid roleId, string permissionCode, CancellationToken cancellationToken = default)
    {
        var role = await _roleManager.FindByIdAsync(roleId.ToString());
        if (role is null)
        {
            return Result<RoleSummaryDto>.Failure(new Error("Roles.NotFound", "Role was not found."));
        }

        var claims = await _roleManager.GetClaimsAsync(role);
        foreach (var claim in claims.Where(x => x.Type == "permission" && string.Equals(x.Value, permissionCode, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            await _roleManager.RemoveClaimAsync(role, claim);
        }

        return Result<RoleSummaryDto>.Success(await ToDto(role));
    }

    public Task<Result<IReadOnlyCollection<string>>> GetAllPermissionsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Result<IReadOnlyCollection<string>>.Success(PermissionCodes.All));
    }

    private async Task<RoleSummaryDto> ToDto(AppRole role)
    {
        var permissions = (await _roleManager.GetClaimsAsync(role))
            .Where(x => x.Type == "permission")
            .Select(x => x.Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new RoleSummaryDto(
            role.Id,
            role.Name ?? string.Empty,
            role.Name ?? string.Empty,
            role.Description ?? string.Empty,
            role.IsSystem,
            true,
            permissions,
            role.CreatedAt,
            null);
    }
}