namespace AutoPartsERP.Api.Modules;

public sealed class ReasonCodesModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reason-codes").RequireAuthorization();

        group.MapPost("/", async Task<IResult> (CreateReasonCodePayload request, ISender sender, CancellationToken cancellationToken) =>
            {
                var command = new CreateReasonCodeCommand(
                    request.Category,
                    request.Code,
                    request.Label,
                    request.RequiresApproval,
                    request.RequiresNotes,
                    request.MinNotesLength,
                    request.RiskLevel);

                var result = await sender.Send(command, cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapGet("/", async Task<IResult> (string category, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetReasonCodesByCategoryQuery(category), cancellationToken);
                return result.ToApiResult();
            });
    }

    public sealed record CreateReasonCodePayload(
        string Category,
        string Code,
        string Label,
        bool RequiresApproval,
        bool RequiresNotes,
        int MinNotesLength,
        string RiskLevel);
}
