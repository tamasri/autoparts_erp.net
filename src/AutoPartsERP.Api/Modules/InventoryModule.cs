namespace AutoPartsERP.Api.Modules;

public sealed class InventoryModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/inventory").RequireAuthorization();

        group.MapGet("/stock", async Task<IResult> (
                int page,
                int pageSize,
                Guid? locationId,
                Guid? skuId,
                string? searchTerm,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetInventoryStockQuery(
                    page <= 0 ? 1 : page,
                    pageSize <= 0 ? 20 : pageSize,
                    locationId,
                    skuId,
                    searchTerm), cancellationToken);
                return result.ToApiResult();
            });

        group.MapGet("/batches", async Task<IResult> (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetBatchesQuery(), cancellationToken);
                return result.ToApiResult();
            });

        group.MapGet("/batches/{id:guid}/trace", async Task<IResult> (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetBatchTraceQuery(id), cancellationToken);
                return result.ToApiResult();
            });

        group.MapGet("/batches/{id:guid}/movements", async Task<IResult> (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetBatchMovementsQuery(id), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/batches/receive", async Task<IResult> (ReceiveBatchRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ReceiveBatchCommand(
                    request.SkuId,
                    request.LocationId,
                    request.Quantity,
                    request.CostPriceSyp,
                    request.CostPriceUsd,
                    request.FxRateId,
                    request.ReceivedDate,
                    request.ExpiryDate,
                    request.SupplierName,
                    request.SupplierInvoice,
                    request.Notes,
                    EndpointRequestHelpers.GetIdempotencyKey(httpContext)), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/adjust", async Task<IResult> (AdjustInventoryRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new AdjustInventoryCommand(
                    request.SkuId,
                    request.LocationId,
                    request.BatchId,
                    request.QuantityDelta,
                    request.Reason,
                    request.Notes,
                    EndpointRequestHelpers.GetIdempotencyKey(httpContext)), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/transfer", async Task<IResult> (TransferStockRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new TransferStockCommand(
                    request.SkuId,
                    request.FromLocationId,
                    request.ToLocationId,
                    request.BatchId,
                    request.Quantity,
                    request.Notes,
                    EndpointRequestHelpers.GetIdempotencyKey(httpContext)), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();
    }
}
