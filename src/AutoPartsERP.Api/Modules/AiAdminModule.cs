namespace AutoPartsERP.Api.Modules;

public sealed class AiAdminModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/ai/admin").RequireAuthorization();

        group.MapGet("/feature-flags", async Task<IResult> (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetAiFeatureFlagsQuery(), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPut("/feature-flags/{code}", async Task<IResult> (string code, UpdateAiFeatureFlagRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new UpdateAiFeatureFlagCommand(code, request), cancellationToken);
                return result.ToApiResult();
            });

        group.MapGet("/scheduled-tasks", async Task<IResult> (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetAiScheduledTasksQuery(), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPut("/scheduled-tasks/{id:guid}", async Task<IResult> (Guid id, UpdateAiScheduledTaskRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new UpdateAiScheduledTaskCommand(id, request), cancellationToken);
                return result.ToApiResult();
            });
    }
}

