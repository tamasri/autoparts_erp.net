namespace AutoPartsERP.Api.Modules;

public sealed class AiModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/ai").RequireAuthorization();

        group.MapPost("/chat", async Task<IResult> (AiChatRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new AiChatCommand(request), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapGet("/suggestions", async Task<IResult> (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetAiSuggestionsQuery(), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/suggestions/{id:guid}/review", async Task<IResult> (Guid id, ReviewAiSuggestionRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new ReviewAiSuggestionCommand(id, request), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapPost("/feedback", async Task<IResult> (SubmitAiFeedbackRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new SubmitAiFeedbackCommand(request), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapGet("/sessions", async Task<IResult> (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetAiSessionsQuery(), cancellationToken);
                return result.ToApiResult();
            });

        group.MapGet("/prompt-logs", async Task<IResult> (int page, int pageSize, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetAiPromptLogsQuery(page <= 0 ? 1 : page, pageSize <= 0 ? 50 : pageSize), cancellationToken);
                return result.ToApiResult();
            });

        group.MapPost("/knowledge/index", async Task<IResult> (IndexKnowledgeDocumentRequest request, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new IndexKnowledgeDocumentCommand(request), cancellationToken);
                return result.ToApiResult();
            })
            .WithIdempotency();

        group.MapGet("/task-runs", async Task<IResult> (int page, int pageSize, ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new GetAiTaskRunsQuery(page <= 0 ? 1 : page, pageSize <= 0 ? 50 : pageSize), cancellationToken);
                return result.ToApiResult();
            });
    }
}

