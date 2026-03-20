namespace AutoPartsERP.Infrastructure.Identity;

public sealed class JwtSettings
{
    public string PublicKeyPemBase64 { get; set; } = string.Empty;

    public string PrivateKeyPemBase64 { get; set; } = string.Empty;

    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    public int AccessTokenExpiryMinutes { get; set; } = 15;

    public int RefreshTokenExpiryDays { get; set; } = 7;
}