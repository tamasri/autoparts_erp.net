namespace AutoPartsERP.Api.Middleware;

public sealed class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    private const string SuperAdminRole = "SUPER_ADMIN";

    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        if (httpContext.User.Identity?.IsAuthenticated == true &&
            httpContext.User.Claims.Any(claim =>
                claim.Type == ClaimTypes.Role &&
                string.Equals(claim.Value, SuperAdminRole, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var bearerHeader = httpContext.Request.Headers.Authorization.ToString();
        if (!bearerHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var token = bearerHeader["Bearer ".Length..].Trim();
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token))
        {
            return false;
        }

        var jwt = handler.ReadJwtToken(token);
        return jwt.Claims.Any(claim =>
            claim.Type is ClaimTypes.Role or "role" &&
            string.Equals(claim.Value, SuperAdminRole, StringComparison.OrdinalIgnoreCase));
    }
}
