namespace AutoPartsERP.Contracts.Ai;

public sealed record AiChatResponseDto(
    Guid SessionId,
    string Response,
    IReadOnlyCollection<string> ToolCallsMade,
    DateTimeOffset TimestampUtc);

public sealed record AiSuggestionDto(
    Guid Id,
    Guid? SessionId,
    string FeatureCode,
    string Title,
    string Content,
    string SuggestedActionCode,
    string Status,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt);

public sealed record AiSessionDto(
    Guid Id,
    string FeatureCode,
    string? Title,
    bool IsActive,
    DateTimeOffset LastInteractionAt,
    DateTimeOffset ExpiresAt);

public sealed record AiPromptLogDto(
    Guid Id,
    Guid? SessionId,
    string FeatureCode,
    string ModelName,
    bool Success,
    int? PromptTokens,
    int? CompletionTokens,
    int LatencyMs,
    DateTimeOffset CreatedAt);

public sealed record AiFeatureFlagDto(
    Guid Id,
    string FeatureCode,
    string Label,
    string ModelName,
    bool IsEnabled,
    string RequiredPermission,
    IReadOnlyCollection<string> AllowedRoles);

public sealed record AiScheduledTaskDto(
    Guid Id,
    string TaskCode,
    string CronExpression,
    bool IsEnabled,
    DateTimeOffset? LastRunAt,
    DateTimeOffset? NextRunAt);

public sealed record AiTaskRunDto(
    Guid Id,
    string TaskCode,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? OutputSummary,
    string? ErrorMessage);

