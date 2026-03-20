namespace AutoPartsERP.Api.Modules;

public sealed class FxRatesModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/fx-rates").RequireAuthorization();

        group.MapGet("/", async Task<IResult> (int page, int pageSize, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(
                    new GetFxRatesQuery(new FxRateQueryRequest(page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize)),
                    cancellationToken);
                return result.ToApiResult();
            });

        group.MapGet("/latest", async Task<IResult> (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetLatestFxRateQuery(), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/", async Task<IResult> (CreateFxRateRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new CreateFxRateCommand(request, EndpointRequestHelpers.GetIdempotencyKey(httpContext)), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();
    }
}
