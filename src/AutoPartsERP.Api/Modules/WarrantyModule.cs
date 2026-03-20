namespace AutoPartsERP.Api.Modules;

public sealed class WarrantyModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/warranty").RequireAuthorization();

        group.MapGet("/", async Task<IResult> (
                int page,
                int pageSize,
                string? status,
                Guid? customerId,
                Guid? skuId,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetWarrantyRecordsQuery(
                    page <= 0 ? 1 : page,
                    pageSize <= 0 ? 20 : pageSize,
                    status,
                    customerId,
                    skuId), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/{id:guid}/claim", async Task<IResult> (Guid id, ClaimWarrantyRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ClaimWarrantyCommand(
                    id,
                    request.Description,
                    request.ClaimDate,
                    EndpointRequestHelpers.GetIdempotencyKey(httpContext)),
                    cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/{id:guid}/process", async Task<IResult> (Guid id, ProcessWarrantyRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ProcessWarrantyCommand(
                    id,
                    request.Resolution,
                    request.ReplacementSkuId,
                    EndpointRequestHelpers.GetIdempotencyKey(httpContext)),
                    cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/{id:guid}/reject", async Task<IResult> (Guid id, RejectWarrantyRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new RejectWarrantyCommand(
                    id,
                    request.Reason,
                    EndpointRequestHelpers.GetIdempotencyKey(httpContext)),
                    cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();
    }
}
