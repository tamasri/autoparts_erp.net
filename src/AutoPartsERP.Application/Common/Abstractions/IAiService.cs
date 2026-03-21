namespace AutoPartsERP.Application.Common.Abstractions;

public sealed record AiChatResult(string Response, IReadOnlyCollection<string> ToolCallsMade, string ModelName, int LatencyMs);

public interface IAiService
{
    Task<Result<AiChatResult>> ChatAsync(
        string featureCode,
        string message,
        Guid userId,
        Guid? sessionId,
        IReadOnlyDictionary<string, object?>? context,
        CancellationToken cancellationToken = default);
}

