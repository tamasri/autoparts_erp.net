namespace AutoPartsERP.Infrastructure.Http;

public sealed class CurrentUserService : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private HashSet<string>? _permissions;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal User => _httpContextAccessor.HttpContext?.User
        ?? throw new InvalidOperationException("No HTTP context is available.");

    public bool IsAuthenticated => User.Identity?.IsAuthenticated == true;

    public Guid UserId => Guid.TryParse(User.FindFirstValue(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub), out var userId)
        ? userId
        : Guid.Empty;

    public string Username => User.FindFirstValue("username") ?? string.Empty;

    public string FullName => User.FindFirstValue("full_name") ?? string.Empty;

    public Guid CorrelationId
    {
        get
        {
            var raw = _httpContextAccessor.HttpContext?.Request.Headers["X-Correlation-ID"].FirstOrDefault();
            return Guid.TryParse(raw, out var parsed) ? parsed : Guid.NewGuid();
        }
    }

    public string? IpAddress => _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent => _httpContextAccessor.HttpContext?.Request.Headers.UserAgent.FirstOrDefault();

    public IReadOnlyCollection<string> Roles => User.FindAll(ClaimTypes.Role).Select(x => x.Value).ToArray();

    public bool HasPermission(string code)
    {
        _permissions ??= User.FindAll("permission").Select(x => x.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _permissions.Contains(code);
    }

    public bool HasRole(string roleCode) => User.IsInRole(roleCode);
}
