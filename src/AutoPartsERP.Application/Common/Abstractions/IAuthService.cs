namespace AutoPartsERP.Application.Common.Abstractions;

public interface IAuthService
{
    Task<Result<AuthTokenResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<Result<AuthTokenResponse>> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task<Result<bool>> LogoutAsync(string refreshToken, CancellationToken cancellationToken = default);

    Task<Result<bool>> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken cancellationToken = default);

    Task<Result<CurrentUserResponse>> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>The same claims a sign-in would put in the access token, for acting as the user inside the server. Fails for locked users.</summary>
    Task<Result<ClaimsPrincipal>> BuildPrincipalAsync(Guid userId, CancellationToken cancellationToken = default);
}
