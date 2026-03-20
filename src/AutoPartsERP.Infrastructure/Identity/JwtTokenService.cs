namespace AutoPartsERP.Infrastructure.Identity;

public sealed class JwtTokenService : ITokenService
{
    private readonly JwtSettings _settings;
    private readonly IConnectionMultiplexer _redis;
    private readonly RsaSecurityKey _privateKey;

    public JwtTokenService(IOptions<JwtSettings> settings, IConnectionMultiplexer redis)
    {
        _settings = settings.Value;
        _redis = redis;

        var rsa = RSA.Create();
        rsa.ImportFromPem(Encoding.UTF8.GetString(Convert.FromBase64String(_settings.PrivateKeyPemBase64)));
        _privateKey = new RsaSecurityKey(rsa);
    }

    public string GenerateAccessToken(TokenGenerationRequest request)
    {
        var claims = new List<Claim>
        {
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, request.UserId.ToString()),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email, request.Email),
            new("username", request.Username),
            new("full_name", request.FullName)
        };

        claims.AddRange(request.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(request.Permissions.Select(permission => new Claim("permission", permission)));

        return CreateToken(claims);
    }

    public string GenerateAccessToken(AutoPartsERP.Domain.Identity.AppUser user, IList<string> roles, IList<Claim> permissionClaims)
    {
        var claims = new List<Claim>
        {
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new("username", user.UserName ?? string.Empty),
            new("full_name", user.FullName)
        };

        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(permissionClaims);

        return CreateToken(claims);
    }

    public async Task<string> GenerateRefreshTokenAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var database = _redis.GetDatabase();
        await database.StringSetAsync($"refresh:{token}", userId.ToString(), TimeSpan.FromDays(_settings.RefreshTokenExpiryDays));
        return token;
    }

    public async Task<Guid?> ValidateRefreshTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var database = _redis.GetDatabase();
        var value = await database.StringGetDeleteAsync($"refresh:{token}");

        if (!value.HasValue || !Guid.TryParse(value!, out var userId))
        {
            return null;
        }

        return userId;
    }

    public async Task InvalidateRefreshTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        var database = _redis.GetDatabase();
        await database.KeyDeleteAsync($"refresh:{token}");
    }

    private string CreateToken(IEnumerable<Claim> claims)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_settings.AccessTokenExpiryMinutes),
            Issuer = _settings.Issuer,
            Audience = _settings.Audience,
            SigningCredentials = new SigningCredentials(_privateKey, SecurityAlgorithms.RsaSha256)
        };

        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(descriptor);
    }
}
