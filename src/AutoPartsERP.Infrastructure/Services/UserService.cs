using AutoPartsERP.Application.Common.Models;
using AutoPartsERP.Infrastructure.Identity;

namespace AutoPartsERP.Infrastructure.Services;

public sealed class UserService : IUserService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly RoleManager<AppRole> _roleManager;

    public UserService(UserManager<AppUser> userManager, RoleManager<AppRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task<Result<PagedResponse<UserSummaryDto>>> GetUsersAsync(UserListFilter filter, CancellationToken cancellationToken = default)
    {
        var query = _userManager.Users.AsQueryable();

        if (filter.IsActive.HasValue && !filter.IsActive.Value)
        {
            query = query.Where(x => x.LockoutEnd.HasValue && x.LockoutEnd > DateTimeOffset.UtcNow);
        }

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(x =>
                x.UserName!.Contains(search) ||
                x.Email!.Contains(search) ||
                x.FullName.Contains(search));
        }

        var total = await query.CountAsync(cancellationToken);
        var users = await query
            .OrderBy(x => x.UserName)
            .Skip((filter.PageNumber - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        var items = new List<UserSummaryDto>(users.Count);
        foreach (var user in users)
        {
            var roles = await GetUserRoleDtos(user, cancellationToken);
            items.Add(ToSummary(user, roles));
        }

        return Result<PagedResponse<UserSummaryDto>>.Success(new PagedResponse<UserSummaryDto>(items, filter.PageNumber, filter.PageSize, total));
    }

    public async Task<Result<UserDetailsDto>> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result<UserDetailsDto>.Failure(new Error("Users.NotFound", "User was not found."));
        }

        return Result<UserDetailsDto>.Success(await ToDetails(user, cancellationToken));
    }

    public async Task<Result<UserDetailsDto>> CreateUserAsync(CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = request.UserName,
            Email = request.Email,
            FullName = string.Concat(request.FirstName, " ", request.LastName).Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        var create = await _userManager.CreateAsync(user, request.Password);
        if (!create.Succeeded)
        {
            return Result<UserDetailsDto>.Failure(new Error("Users.CreateFailed", string.Join("; ", create.Errors.Select(x => x.Description))));
        }

        var roleNames = await ResolveRoleNames(request.RoleIds, cancellationToken);
        if (roleNames.Count > 0)
        {
            await _userManager.AddToRolesAsync(user, roleNames);
        }

        return Result<UserDetailsDto>.Success(await ToDetails(user, cancellationToken));
    }

    public async Task<Result<UserDetailsDto>> UpdateUserAsync(Guid userId, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result<UserDetailsDto>.Failure(new Error("Users.NotFound", "User was not found."));
        }

        user.Email = request.Email;
        user.FullName = string.Concat(request.FirstName, " ", request.LastName).Trim();

        if (!request.IsActive)
        {
            user.LockoutEnd = DateTimeOffset.MaxValue;
        }

        var update = await _userManager.UpdateAsync(user);
        if (!update.Succeeded)
        {
            return Result<UserDetailsDto>.Failure(new Error("Users.UpdateFailed", string.Join("; ", update.Errors.Select(x => x.Description))));
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        if (currentRoles.Count > 0)
        {
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
        }

        var requestedRoles = await ResolveRoleNames(request.RoleIds, cancellationToken);
        if (requestedRoles.Count > 0)
        {
            await _userManager.AddToRolesAsync(user, requestedRoles);
        }

        return Result<UserDetailsDto>.Success(await ToDetails(user, cancellationToken));
    }

    public async Task<Result<UserDetailsDto>> SetUserLockAsync(Guid userId, SetUserLockRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result<UserDetailsDto>.Failure(new Error("Users.NotFound", "User was not found."));
        }

        await _userManager.SetLockoutEndDateAsync(user, request.LockoutEndUtc);
        return Result<UserDetailsDto>.Success(await ToDetails(user, cancellationToken));
    }

    public async Task<Result<UserDetailsDto>> AssignRolesAsync(Guid userId, AssignUserRolesRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result<UserDetailsDto>.Failure(new Error("Users.NotFound", "User was not found."));
        }

        var currentRoles = await _userManager.GetRolesAsync(user);
        if (currentRoles.Count > 0)
        {
            await _userManager.RemoveFromRolesAsync(user, currentRoles);
        }

        var requestedRoles = await ResolveRoleNames(request.RoleIds, cancellationToken);
        if (requestedRoles.Count > 0)
        {
            await _userManager.AddToRolesAsync(user, requestedRoles);
        }

        return Result<UserDetailsDto>.Success(await ToDetails(user, cancellationToken));
    }

    public async Task<Result<UserDetailsDto>> DeactivateUserAsync(Guid userId, string reason, string? reasonCode, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result<UserDetailsDto>.Failure(new Error("Users.NotFound", "User was not found."));
        }

        await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);
        await _userManager.UpdateSecurityStampAsync(user);

        return Result<UserDetailsDto>.Success(await ToDetails(user, cancellationToken));
    }

    private async Task<IReadOnlyCollection<string>> ResolveRoleNames(IReadOnlyCollection<Guid> roleIds, CancellationToken cancellationToken)
    {
        if (roleIds.Count == 0)
        {
            return Array.Empty<string>();
        }

        var roleNames = await _roleManager.Roles
            .Where(x => roleIds.Contains(x.Id))
            .Select(x => x.Name!)
            .ToListAsync(cancellationToken);

        return roleNames;
    }

    private async Task<IReadOnlyCollection<UserRoleDto>> GetUserRoleDtos(AppUser user, CancellationToken cancellationToken)
    {
        var roleNames = await _userManager.GetRolesAsync(user);
        var roles = await _roleManager.Roles.Where(x => roleNames.Contains(x.Name!)).ToListAsync(cancellationToken);
        return roles.Select(x => new UserRoleDto(x.Id, x.Name ?? string.Empty, x.Name ?? string.Empty)).ToArray();
    }

    private async Task<IReadOnlyCollection<string>> GetPermissions(AppUser user)
    {
        var roleNames = await _userManager.GetRolesAsync(user);
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var roleName in roleNames)
        {
            var role = await _roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                continue;
            }

            var claims = await _roleManager.GetClaimsAsync(role);
            foreach (var claim in claims.Where(x => x.Type == "permission"))
            {
                permissions.Add(claim.Value);
            }
        }

        return permissions.ToArray();
    }

    private async Task<UserDetailsDto> ToDetails(AppUser user, CancellationToken cancellationToken)
    {
        var roles = await GetUserRoleDtos(user, cancellationToken);
        var permissions = await GetPermissions(user);
        var summary = ToSummary(user, roles);

        return new UserDetailsDto(
            summary.Id,
            summary.UserName,
            summary.Email,
            summary.FirstName,
            summary.LastName,
            summary.IsActive,
            summary.IsLockedOut,
            summary.Roles,
            permissions,
            summary.CreatedAtUtc,
            summary.LastLoginAtUtc,
            DateTimeOffset.UtcNow);
    }

    private static UserSummaryDto ToSummary(AppUser user, IReadOnlyCollection<UserRoleDto> roles)
    {
        var parts = (user.FullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var first = parts.Length > 0 ? parts[0] : string.Empty;
        var last = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : string.Empty;

        return new UserSummaryDto(
            user.Id,
            user.UserName ?? string.Empty,
            user.Email ?? string.Empty,
            first,
            last,
            true,
            user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow,
            roles,
            user.CreatedAt,
            user.LastLoginAt);
    }
}