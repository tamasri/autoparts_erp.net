namespace AutoPartsERP.Api.Modules;

public sealed class CycleCountsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/cycle-counts").RequireAuthorization();

        group.MapGet("/", async Task<IResult> (int page, int pageSize, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetCycleCountPlansQuery(page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/", async Task<IResult> (CreateCycleCountPlanRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new CreateCycleCountPlanCommand(request), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/{id:guid}/record", async Task<IResult> (Guid id, RecordCycleCountRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new RecordCycleCountCommand(request with { CycleCountPlanId = id }), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/{id:guid}/approve-variance", async Task<IResult> (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ApproveCycleCountVarianceCommand(id), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();
    }
}

