namespace AutoPartsERP.Api.Modules;

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
                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithIdempotency();

        group.MapPost("/refresh", async Task<IResult> (RefreshTokenRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new RefreshTokenCommand(request.RefreshToken), cancellationToken);
                return result.ToApiResult();
            })
            .AllowAnonymous()
            .WithIdempotency();

        group.MapPost("/logout", async Task<IResult> (LogoutRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new LogoutCommand(request.RefreshToken), cancellationToken);
                return result.ToApiResult();
            })
            .RequireAuthorization()
            .WithIdempotency();

        group.MapGet("/me", async Task<IResult> (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetCurrentUserQuery(), cancellationToken);
                return result.ToApiResult();
            })
            .RequireAuthorization();
    }
}
