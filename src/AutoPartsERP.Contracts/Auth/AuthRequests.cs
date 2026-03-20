namespace AutoPartsERP.Contracts.Auth;

public sealed record LoginRequest(string UserNameOrEmail, string Password, string? IpAddress = null, string? UserAgent = null);

public sealed record RefreshTokenRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
