namespace AutoPartsERP.Api.Modules;

public sealed class StockAdjustmentsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/stock-adjustments").RequireAuthorization();

        group.MapGet("/", async Task<IResult> (int page, int pageSize, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetStockAdjustmentsQuery(page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/", async Task<IResult> (CreateStockAdjustmentRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new CreateStockAdjustmentCommand(request), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/{id:guid}/post", async Task<IResult> (Guid id, DateOnly? operationDate, ISender sender, CancellationToken cancellationToken) =>
            {
                var effectiveDate = operationDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
                var result = await sender.Send(new PostStockAdjustmentCommand(id, effectiveDate), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();
    }
}

