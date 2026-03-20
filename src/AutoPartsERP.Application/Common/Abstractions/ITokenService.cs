using AutoPartsERP.Application.Common.Models;

namespace AutoPartsERP.Application.Common.Abstractions;

public interface ITokenService
{
    string GenerateAccessToken(TokenGenerationRequest request);

    string GenerateAccessToken(AppUser user, IList<string> roles, IList<Claim> permissionClaims);

    Task<string> GenerateRefreshTokenAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<Guid?> ValidateRefreshTokenAsync(string token, CancellationToken cancellationToken = default);

    Task InvalidateRefreshTokenAsync(string token, CancellationToken cancellationToken = default);
}
