namespace AutoPartsERP.Api.Extensions;

public static class IdempotencyEndpointExtensions
{
    public static IServiceCollection AddApiIdempotency(this IServiceCollection services)
    {
        return services;
    }

    public static IApplicationBuilder UseApiIdempotency(this IApplicationBuilder app)
    {
        return app;
    }

    public static RouteHandlerBuilder WithIdempotency(this RouteHandlerBuilder builder)
    {
        return builder;
    }
}
