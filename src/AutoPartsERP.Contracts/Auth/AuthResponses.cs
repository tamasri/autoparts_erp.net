using AutoPartsERP.Contracts.Users;

namespace AutoPartsERP.Contracts.Auth;

public sealed record AuthTokenResponse(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    DateTimeOffset RefreshTokenExpiresAtUtc,
    UserSummaryDto User,
    IReadOnlyCollection<string> Permissions);

/// <summary>
/// What the browser receives on login and refresh. The refresh token is NOT in it: it travels only in an HttpOnly cookie,
/// so script on the page (including injected script) can never read it.
/// </summary>
public sealed record AuthSessionResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    UserSummaryDto User,
    IReadOnlyCollection<string> Permissions);

public sealed record CurrentUserResponse(
    UserSummaryDto User,
    IReadOnlyCollection<string> Permissions);
