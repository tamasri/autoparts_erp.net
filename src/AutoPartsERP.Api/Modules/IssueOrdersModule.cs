namespace AutoPartsERP.Api.Modules;

public sealed class IssueOrdersModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/issue-orders").RequireAuthorization();

        group.MapGet("/", async Task<IResult> (int page, int pageSize, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetIssueOrdersQuery(page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/", async Task<IResult> (CreateIssueOrderCommand command, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(command, cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/{id:guid}/pick-tasks/generate", async Task<IResult> (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GeneratePickTasksCommand(id), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/{id:guid}/pick-tasks/{taskId:guid}/complete", async Task<IResult> (Guid id, Guid taskId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new CompletePickTaskCommand(id, taskId), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/{id:guid}/pick-tasks/{taskId:guid}/verify", async Task<IResult> (Guid id, Guid taskId, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new VerifyPickTaskCommand(id, taskId), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/{id:guid}/issue", async Task<IResult> (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new IssueOrderCommand(id), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();
    }
}

