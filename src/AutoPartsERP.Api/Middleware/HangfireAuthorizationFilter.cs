using AutoPartsERP.Domain.Constants;

namespace AutoPartsERP.Api.Middleware;

public sealed class HangfireAuthorizationFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var httpContext = context.GetHttpContext();

        // ASP.NET Core's authentication middleware (app.UseAuthentication(), registered before the
        // Hangfire dashboard route) has already validated the bearer JWT's signature, issuer, audience,
        // and expiry by the time this filter runs. Trust httpContext.User only — never re-parse the
        // raw token here (the previous implementation used JwtSecurityTokenHandler.ReadJwtToken, which
        // decodes claims WITHOUT verifying the signature, letting anyone forge a role claim).
        return httpContext.User.Identity?.IsAuthenticated == true
            && httpContext.User.IsInRole(RoleCodes.SystemAdministrator);
    }
}
