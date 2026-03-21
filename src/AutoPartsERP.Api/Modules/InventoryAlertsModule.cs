namespace AutoPartsERP.Api.Modules;

public sealed class InventoryAlertsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/inventory/alerts").RequireAuthorization();

        group.MapGet("/", async Task<IResult> (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetInventoryAlertsQuery(), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/{id:guid}/acknowledge", async Task<IResult> (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new AcknowledgeAlertCommand(id), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/{id:guid}/resolve", async Task<IResult> (Guid id, ResolveInventoryAlertRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ResolveAlertCommand(id, request), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();
    }
}

