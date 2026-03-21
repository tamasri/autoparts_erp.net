namespace AutoPartsERP.Api.Modules;

public sealed class ItemsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/items").RequireAuthorization();

        group.MapGet("/search", async Task<IResult> (
                string query,
                int page,
                int pageSize,
                bool includeInactive,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new SearchItemsQuery(new SearchItemsRequest(
                    query,
                    page <= 0 ? 1 : page,
                    pageSize <= 0 ? 20 : pageSize,
                    includeInactive)), cancellationToken);
                return result.ToApiResult();
            });

        group.MapGet("/{id:guid}", async Task<IResult> (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetItemByIdQuery(id), cancellationToken);
                return result.ToApiResult();
            });

        group.MapGet("/by-part/{number}", async Task<IResult> (string number, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetItemByPartNumberQuery(number), cancellationToken);
                return result.ToApiResult();
            });

        group.MapGet("/by-barcode/{code}", async Task<IResult> (string code, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetItemByBarcodeQuery(code), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/", async Task<IResult> (CreateItemRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new CreateItemCommand(request, EndpointRequestHelpers.GetIdempotencyKey(httpContext)), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPut("/{id:guid}", async Task<IResult> (Guid id, UpdateItemRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new UpdateItemCommand(id, request), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/{id:guid}/aliases", async Task<IResult> (Guid id, AddItemAliasRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new AddItemAliasCommand(id, request), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/{id:guid}/interchanges", async Task<IResult> (Guid id, AddItemInterchangeRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new AddItemInterchangeCommand(id, request), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/{id:guid}/stop-ship", async Task<IResult> (Guid id, MarkItemStopShipRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new MarkItemStopShipCommand(id, request), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapGet("/{id:guid}/interchanges", async Task<IResult> (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetItemInterchangesQuery(id), cancellationToken);
                return result.ToApiResult();
            });
    }
}

