using AutoPartsERP.Infrastructure.Identity;

namespace AutoPartsERP.Infrastructure.Services;

public sealed class AuthService : IAuthService
{
    private readonly UserManager<AppUser> _userManager;
    private readonly RoleManager<AppRole> _roleManager;
    private readonly ITokenService _tokenService;
    private readonly IManualAuditService _manualAuditService;

    public AuthService(
        UserManager<AppUser> userManager,
        RoleManager<AppRole> roleManager,
        ITokenService tokenService,
        IManualAuditService manualAuditService)
    {
        _userManager = userManager;
        _roleManager = roleManager;
        _tokenService = tokenService;
        _manualAuditService = manualAuditService;
    }

    public async Task<Result<AuthTokenResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var normalized = request.UserNameOrEmail.ToUpperInvariant();
        var user = await _userManager.Users.FirstOrDefaultAsync(
            x => x.NormalizedUserName == normalized || x.NormalizedEmail == normalized,
            cancellationToken);

        if (user is null)
        {
            return Result<AuthTokenResponse>.Failure(new Error("Auth.InvalidCredentials", "Invalid username or password."));
        }

        if (user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow)
        {
            return Result<AuthTokenResponse>.Failure(new Error("Auth.LockedOut", "Account is locked out."));
        }

        var passwordOk = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!passwordOk)
        {
            await _userManager.AccessFailedAsync(user);
            await _manualAuditService.LogAsync(new ManualAuditEntry(
                Guid.NewGuid(),
                AuditActions.Login,
                "AUTH",
                nameof(AppUser),
                user.Id,
                user.Id,
                user.UserName ?? string.Empty,
                "LOGIN",
                "FAILED",
                IpAddress: request.IpAddress,
                UserAgent: request.UserAgent), cancellationToken);

            return Result<AuthTokenResponse>.Failure(new Error("Auth.InvalidCredentials", "Invalid username or password."));
        }

        await _userManager.ResetAccessFailedCountAsync(user);

        var roles = await _userManager.GetRolesAsync(user);
        var permissions = await ResolvePermissionsAsync(roles);

        var accessToken = _tokenService.GenerateAccessToken(new TokenGenerationRequest(
            user.Id,
            user.UserName ?? string.Empty,
            user.FullName,
            user.Email ?? string.Empty,
            roles.ToArray(),
            permissions));
        var refreshToken = await _tokenService.GenerateRefreshTokenAsync(user.Id, cancellationToken);

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await _userManager.UpdateAsync(user);

        await _manualAuditService.LogAsync(new ManualAuditEntry(
            Guid.NewGuid(),
            AuditActions.Login,
            "AUTH",
            nameof(AppUser),
            user.Id,
            user.Id,
            user.UserName ?? string.Empty,
            "LOGIN",
            "SUCCESS",
            IpAddress: request.IpAddress,
            UserAgent: request.UserAgent), cancellationToken);

        var userDto = ToUserSummary(user, roles.Select(x => new UserRoleDto(Guid.Empty, x, x)).ToArray());
        var response = new AuthTokenResponse(
            accessToken,
            refreshToken,
            DateTimeOffset.UtcNow.AddMinutes(15),
            DateTimeOffset.UtcNow.AddDays(7),
            userDto,
            permissions);

        return Result<AuthTokenResponse>.Success(response);
    }

    public async Task<Result<AuthTokenResponse>> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        var userId = await _tokenService.ValidateRefreshTokenAsync(refreshToken, cancellationToken);
        if (!userId.HasValue)
        {
            return Result<AuthTokenResponse>.Failure(new Error("Auth.InvalidRefreshToken", "Refresh token is invalid or expired."));
        }

        var user = await _userManager.FindByIdAsync(userId.Value.ToString());
        if (user is null)
        {
            return Result<AuthTokenResponse>.Failure(new Error("Auth.UserNotFound", "User was not found."));
        }

        var roles = await _userManager.GetRolesAsync(user);
        var permissions = await ResolvePermissionsAsync(roles);

        var accessToken = _tokenService.GenerateAccessToken(new TokenGenerationRequest(
            user.Id,
            user.UserName ?? string.Empty,
            user.FullName,
            user.Email ?? string.Empty,
            roles.ToArray(),
            permissions));

        var newRefreshToken = await _tokenService.GenerateRefreshTokenAsync(user.Id, cancellationToken);
        var userDto = ToUserSummary(user, roles.Select(x => new UserRoleDto(Guid.Empty, x, x)).ToArray());

        var response = new AuthTokenResponse(
            accessToken,
            newRefreshToken,
            DateTimeOffset.UtcNow.AddMinutes(15),
            DateTimeOffset.UtcNow.AddDays(7),
            userDto,
            permissions);

        return Result<AuthTokenResponse>.Success(response);
    }

    public async Task<Result<bool>> LogoutAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        await _tokenService.InvalidateRefreshTokenAsync(refreshToken, cancellationToken);
        return Result<bool>.Success(true);
    }

    public async Task<Result<bool>> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result<bool>.Failure(new Error("Auth.UserNotFound", "User was not found."));
        }

        var changed = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!changed.Succeeded)
        {
            var message = string.Join("; ", changed.Errors.Select(x => x.Description));
            return Result<bool>.Failure(new Error("Auth.PasswordChangeFailed", message));
        }

        return Result<bool>.Success(true);
    }

    public async Task<Result<CurrentUserResponse>> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return Result<CurrentUserResponse>.Failure(new Error("Auth.UserNotFound", "User was not found."));
        }

        var roles = await _userManager.GetRolesAsync(user);
        var permissions = await ResolvePermissionsAsync(roles);
        var summary = ToUserSummary(user, roles.Select(x => new UserRoleDto(Guid.Empty, x, x)).ToArray());

        return Result<CurrentUserResponse>.Success(new CurrentUserResponse(summary, permissions));
    }

    private async Task<IReadOnlyCollection<string>> ResolvePermissionsAsync(IEnumerable<string> roles)
    {
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var roleName in roles)
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

    private static UserSummaryDto ToUserSummary(AppUser user, IReadOnlyCollection<UserRoleDto> roles)
    {
        var parts = (user.FullName ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var firstName = parts.Length > 0 ? parts[0] : string.Empty;
        var lastName = parts.Length > 1 ? string.Join(' ', parts.Skip(1)) : string.Empty;

        return new UserSummaryDto(
            user.Id,
            user.UserName ?? string.Empty,
            user.Email ?? string.Empty,
            firstName,
            lastName,
            true,
            user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow,
            roles,
            user.CreatedAt,
            user.LastLoginAt);
    }
}
