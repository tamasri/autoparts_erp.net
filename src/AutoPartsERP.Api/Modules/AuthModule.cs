namespace AutoPartsERP.Api.Modules;

/// <summary>
/// Sign-in endpoints. The refresh token is set and read only as an HttpOnly cookie (<see cref="RefreshCookie"/>); the JSON
/// answers carry the short-lived access token, the user and the permissions. These endpoints are not idempotent-cached:
/// their answers are credentials and set a cookie, which a replayed answer could not reproduce.
/// </summary>
public sealed class AuthModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth");

        group.MapPost("/login", async Task<IResult> (LoginRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var enrichedRequest = request with
                {
                    IpAddress = httpContext.Connection.RemoteIpAddress?.ToString(),
                    UserAgent = httpContext.Request.Headers.UserAgent.ToString()
                };

                var result = await sender.Send(new LoginCommand(enrichedRequest), cancellationToken);
                return StartSession(httpContext, result);
            })
            .AllowAnonymous();

        group.MapPost("/refresh", async Task<IResult> (HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                if (!RefreshCookie.HasCsrfHeader(httpContext))
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Auth.CsrfHeaderMissing", detail: "Missing request header.");
                }

                var token = RefreshCookie.Read(httpContext);
                if (token is null)
                {
                    return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Auth.NoSession", detail: "Not signed in.");
                }

                var result = await sender.Send(new RefreshTokenCommand(token), cancellationToken);
                if (result.IsFailure)
                {
                    RefreshCookie.Clear(httpContext);
                    return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: result.Error.Code, detail: result.Error.Message);
                }

                return StartSession(httpContext, result);
            })
            .AllowAnonymous();

        // Anonymous on purpose: signing out must work after the access token has expired. The cookie identifies the session.
        group.MapPost("/logout", async Task<IResult> (HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                if (!RefreshCookie.HasCsrfHeader(httpContext))
                {
                    return Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Auth.CsrfHeaderMissing", detail: "Missing request header.");
                }

                if (RefreshCookie.Read(httpContext) is { } token)
                {
                    await sender.Send(new LogoutCommand(token), cancellationToken);
                }

                RefreshCookie.Clear(httpContext);
                return Results.Ok(ApiResponse.Success(true));
            })
            .AllowAnonymous();

        group.MapGet("/me", async Task<IResult> (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetCurrentUserQuery(), cancellationToken);
                return result.ToApiResult();
            })
            .RequireAuthorization();
    }

    private static IResult StartSession(HttpContext httpContext, Result<AuthTokenResponse> result)
    {
        if (result.IsFailure || result.Value is not { } tokens)
        {
            return result.ToApiResult();
        }

        RefreshCookie.Write(httpContext, tokens.RefreshToken, tokens.RefreshTokenExpiresAtUtc);
        return Results.Ok(ApiResponse.Success(new AuthSessionResponse(tokens.AccessToken, tokens.AccessTokenExpiresAtUtc, tokens.User, tokens.Permissions)));
    }
}
