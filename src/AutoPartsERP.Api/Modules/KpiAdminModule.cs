namespace AutoPartsERP.Api.Modules;

public sealed class KpiAdminModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/kpi/admin").RequireAuthorization();

        group.MapGet("/definitions", async Task<IResult> (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetKpiDefinitionsQuery(), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/definitions", async Task<IResult> (CreateKpiDefinitionRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new CreateKpiDefinitionCommand(request), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/definitions/{id:guid}/threshold", async Task<IResult> (
                Guid id,
                SetKpiThresholdRequest request,
                HttpContext httpContext,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new SetKpiThresholdCommand(
                    id,
                    request,
                    EndpointRequestHelpers.GetIdempotencyKey(httpContext)), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();
    }
}
