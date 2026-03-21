namespace AutoPartsERP.Api.Modules;

public sealed class ReceivingModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/receiving").RequireAuthorization();

        group.MapGet("/", async Task<IResult> (int page, int pageSize, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetReceivingDocumentsQuery(page <= 0 ? 1 : page, pageSize <= 0 ? 20 : pageSize), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/", async Task<IResult> (CreateReceivingDocumentRequest request, HttpContext httpContext, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new CreateReceivingDocumentCommand(request, EndpointRequestHelpers.GetIdempotencyKey(httpContext)), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/{id:guid}/lines", async Task<IResult> (Guid id, AddReceivingLineRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new AddReceivingLineCommand(id, request), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/{id:guid}/post", async Task<IResult> (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new PostReceivingDocumentCommand(id), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapGet("/{id:guid}/putaway-tasks", async Task<IResult> (Guid id, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetPutawayTasksQuery(id), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/putaway/{taskId:guid}/complete", async Task<IResult> (Guid taskId, CompletePutawayTaskRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new CompletePutawayTaskCommand(taskId, request), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();
    }
}

