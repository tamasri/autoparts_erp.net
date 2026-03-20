using AutoPartsERP.Contracts.Users;

namespace AutoPartsERP.Contracts.Auth;

public sealed record AuthTokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    UserSummaryDto User,
    IReadOnlyCollection<string> Permissions);

public sealed record CurrentUserResponse(
    UserSummaryDto User,
    IReadOnlyCollection<string> Permissions);
