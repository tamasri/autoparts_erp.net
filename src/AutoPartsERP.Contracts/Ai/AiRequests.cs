namespace AutoPartsERP.Contracts.Ai;

public sealed record AiChatRequest(
    string Feature,
    string Message,
    Guid? SessionId,
    IReadOnlyDictionary<string, object?>? Context);

public sealed record ReviewAiSuggestionRequest(
    string Decision,
    string? Notes);

public sealed record SubmitAiFeedbackRequest(
    Guid PromptLogId,
    int Rating,
    bool WasHelpful,
    string? Correction);

public sealed record IndexKnowledgeDocumentRequest(
    string Title,
    string SourcePath,
    string ContentText,
    string LanguageCode = "ar");

public sealed record UpdateAiFeatureFlagRequest(
    bool IsEnabled,
    string ModelName,
    string RequiredPermission,
    IReadOnlyCollection<string> AllowedRoles);

public sealed record UpdateAiScheduledTaskRequest(
    string CronExpression,
    bool IsEnabled);

