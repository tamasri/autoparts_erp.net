namespace AutoPartsERP.Infrastructure.Http;

/// <summary>
/// Makes the current request act as the linked ERP user: the request's principal becomes that user's (same claims as their access
/// token), so <see cref="CurrentUserService"/>, the authorization behavior, warehouse scope and audit all see that user.
/// Only the internal assistant endpoint uses it, after the gateway secret was checked.
/// </summary>
public sealed class AssistantIdentity : IAssistantIdentity
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IAuthService _authService;

    public AssistantIdentity(IHttpContextAccessor httpContextAccessor, IAuthService authService)
    {
        _httpContextAccessor = httpContextAccessor;
        _authService = authService;
    }

    public async Task<Result> ActAsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var context = _httpContextAccessor.HttpContext ?? throw new InvalidOperationException("No HTTP context is available.");
        var principal = await _authService.BuildPrincipalAsync(userId, cancellationToken);
        if (principal.IsFailure)
        {
            return Result.Failure(principal.Error);
        }

        context.User = principal.Value!;
        return Result.Success();
    }
}
