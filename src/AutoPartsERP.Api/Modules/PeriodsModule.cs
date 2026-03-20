namespace AutoPartsERP.Api.Modules;

public sealed class PeriodsModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/periods").RequireAuthorization();

        group.MapPost("/lock", async Task<IResult> (LockPeriodRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                if (!EndpointRequestHelpers.TryParsePeriodKey(request.PeriodKey, out var year, out var month))
                {
                    return Results.Problem(
                        title: "Validation.InvalidPeriod",
                        detail: "PeriodKey must be in the form yyyy-MM.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var result = await sender.Send(new LockPeriodCommand(year, month, request.ModuleCode, request.Reason), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/unlock", async Task<IResult> (UnlockPeriodPayload request, ISender sender, CancellationToken cancellationToken) =>
            {
                if (!EndpointRequestHelpers.TryParsePeriodKey(request.PeriodKey, out var year, out var month))
                {
                    return Results.Problem(
                        title: "Validation.InvalidPeriod",
                        detail: "PeriodKey must be in the form yyyy-MM.",
                        statusCode: StatusCodes.Status400BadRequest);
                }

                var result = await sender.Send(new UnlockPeriodCommand(year, month, request.ModuleCode, request.Reason), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapGet("/locks", async Task<IResult> (int? year, int? month, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetPeriodLocksQuery(year, month), cancellationToken);
                return result.ToApiResult();
            });
    }

    public sealed record UnlockPeriodPayload(string PeriodKey, string ModuleCode, string Reason);
}
