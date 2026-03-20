namespace AutoPartsERP.Api.Modules;

public sealed class CatalogModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/catalog").RequireAuthorization();

        group.MapGet("/categories", async Task<IResult> (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetCategoryTreeQuery(), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/categories", async Task<IResult> (CreateCategoryRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new CreateCategoryCommand(request, EndpointRequestHelpers.GetIdempotencyKey(httpContext)), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapGet("/skus", async Task<IResult> (
                int page,
                int pageSize,
                Guid? categoryId,
                bool? isActive,
                bool? isBatchTracked,
                bool? hasWarranty,
                string[]? tags,
                string? searchTerm,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetSkusQuery(
                    new SkuQueryRequest(
                        page <= 0 ? 1 : page,
                        pageSize <= 0 ? 20 : pageSize,
                        categoryId,
                        isActive,
                        isBatchTracked,
                        hasWarranty,
                        tags,
                        searchTerm)), cancellationToken);
                return result.ToApiResult();
            });

        group.MapGet("/skus/{id:guid}", async Task<IResult> (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetSkuByIdQuery(id), cancellationToken);
                return result.ToApiResult();
            });

        group.MapGet("/skus/by-code/{code}", async Task<IResult> (string code, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetSkuByCodeQuery(code), cancellationToken);
                return result.ToApiResult();
            });

        group.MapGet("/skus/by-barcode/{barcode}", async Task<IResult> (string barcode, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetSkuByBarcodeQuery(barcode), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/skus", async Task<IResult> (CreateSkuRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new CreateSkuCommand(request, EndpointRequestHelpers.GetIdempotencyKey(httpContext)), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPut("/skus/{id:guid}/prices", async Task<IResult> (Guid id, UpdateSkuPricesRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new UpdateSkuPricesCommand(id, request), cancellationToken);
                return result.ToApiResult();
            });
    }
}
