namespace AutoPartsERP.Api.Modules;

public sealed class TransfersModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/transfers").RequireAuthorization();

        group.MapGet("/requests", async Task<IResult> (int page, int pageSize, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetTransferRequestsQuery(page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/requests", async Task<IResult> (CreateTransferRequestCommand command, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(command, cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapGet("/orders", async Task<IResult> (int page, int pageSize, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetTransferOrdersQuery(page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/orders", async Task<IResult> (CreateTransferOrderRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new CreateTransferOrderCommand(request), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/orders/{id:guid}/ship", async Task<IResult> (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ShipTransferOrderCommand(id), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/orders/{id:guid}/receive", async Task<IResult> (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ReceiveTransferOrderCommand(id), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();
    }
}

