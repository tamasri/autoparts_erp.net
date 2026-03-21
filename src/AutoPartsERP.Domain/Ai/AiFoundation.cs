namespace AutoPartsERP.Domain.Ai;

public sealed class AiFeatureFlag : AuditableEntity
{
    public AiFeatureFlag(
        Guid id,
        string featureCode,
        string label,
        string modelName,
        bool isEnabled,
        string requiredPermission,
        string[] allowedRoles,
        Guid createdBy)
        : base(id)
    {
        FeatureCode = featureCode;
        Label = label;
        ModelName = modelName;
        IsEnabled = isEnabled;
        RequiredPermission = requiredPermission;
        AllowedRoles = allowedRoles;
        CreatedBy = createdBy;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    private AiFeatureFlag()
        : base(Guid.NewGuid())
    {
    }

    public string FeatureCode { get; private set; } = string.Empty;

    public string Label { get; private set; } = string.Empty;

    public string ModelName { get; private set; } = string.Empty;

    public bool IsEnabled { get; private set; }

    public string RequiredPermission { get; private set; } = string.Empty;

    public string[] AllowedRoles { get; private set; } = Array.Empty<string>();

    public bool AllowWritesToCoreData { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public void Update(bool isEnabled, string modelName, string requiredPermission, string[] allowedRoles, Guid by)
    {
        IsEnabled = isEnabled;
        ModelName = string.IsNullOrWhiteSpace(modelName) ? ModelName : modelName.Trim();
        RequiredPermission = string.IsNullOrWhiteSpace(requiredPermission) ? RequiredPermission : requiredPermission.Trim();
        AllowedRoles = allowedRoles.Length == 0 ? Array.Empty<string>() : allowedRoles;
        UpdatedBy = by;
        Touch();
    }
}

public sealed class AiSession : AuditableEntity
{
    public AiSession(Guid id, Guid userId, string featureCode, string? title)
        : base(id)
    {
        UserId = userId;
        FeatureCode = featureCode;
        Title = title;
        IsActive = true;
        LastInteractionAt = DateTimeOffset.UtcNow;
        ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(30);
        CreatedAt = DateTimeOffset.UtcNow;
    }

    private AiSession()
        : base(Guid.NewGuid())
    {
    }

    public Guid UserId { get; private set; }

    public string FeatureCode { get; private set; } = string.Empty;

    public string? Title { get; private set; }

    public string ContextJson { get; private set; } = "{}";

    public bool IsActive { get; private set; }

    public DateTimeOffset LastInteractionAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void TouchInteraction(TimeSpan ttl)
    {
        LastInteractionAt = DateTimeOffset.UtcNow;
        ExpiresAt = DateTimeOffset.UtcNow.Add(ttl);
        Touch();
    }
}

public sealed class AiPromptLog : AuditableEntity
{
    public AiPromptLog(
        Guid id,
        Guid? sessionId,
        Guid userId,
        string featureCode,
        string modelName,
        string promptText,
        string responseText,
        bool success,
        int? promptTokens,
        int? completionTokens,
        int latencyMs,
        string? toolCallsJson)
        : base(id)
    {
        SessionId = sessionId;
        UserId = userId;
        FeatureCode = featureCode;
        ModelName = modelName;
        PromptText = promptText;
        ResponseText = responseText;
        Success = success;
        PromptTokens = promptTokens;
        CompletionTokens = completionTokens;
        LatencyMs = latencyMs;
        ToolCallsJson = toolCallsJson;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    private AiPromptLog()
        : base(Guid.NewGuid())
    {
    }

    public Guid? SessionId { get; private set; }

    public Guid UserId { get; private set; }

    public string FeatureCode { get; private set; } = string.Empty;

    public string ModelName { get; private set; } = string.Empty;

    public string PromptText { get; private set; } = string.Empty;

    public string ResponseText { get; private set; } = string.Empty;

    public bool Success { get; private set; }

    public int? PromptTokens { get; private set; }

    public int? CompletionTokens { get; private set; }

    public int LatencyMs { get; private set; }

    public string? ToolCallsJson { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class AiSuggestion : AuditableEntity
{
    public AiSuggestion(
        Guid id,
        Guid? sessionId,
        Guid userId,
        string featureCode,
        string title,
        string content,
        string suggestedActionCode,
        string payloadJson,
        DateTimeOffset expiresAt)
        : base(id)
    {
        SessionId = sessionId;
        UserId = userId;
        FeatureCode = featureCode;
        Title = title;
        Content = content;
        SuggestedActionCode = suggestedActionCode;
        PayloadJson = payloadJson;
        Status = "PENDING";
        ExpiresAt = expiresAt;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    private AiSuggestion()
        : base(Guid.NewGuid())
    {
    }

    public Guid? SessionId { get; private set; }

    public Guid UserId { get; private set; }

    public string FeatureCode { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string Content { get; private set; } = string.Empty;

    public string SuggestedActionCode { get; private set; } = string.Empty;

    public string PayloadJson { get; private set; } = "{}";

    public string Status { get; private set; } = "PENDING";

    public DateTimeOffset ExpiresAt { get; private set; }

    public Guid? ReviewedBy { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public string? ReviewNotes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Review(string decision, string? notes, Guid by)
    {
        Status = decision;
        ReviewNotes = notes;
        ReviewedBy = by;
        ReviewedAt = DateTimeOffset.UtcNow;
        Touch();
    }
}

public sealed class AiTaskRun : AuditableEntity
{
    public AiTaskRun(Guid id, string taskCode, string status, DateTimeOffset startedAt, DateTimeOffset? completedAt, string? outputSummary, string? errorMessage)
        : base(id)
    {
        TaskCode = taskCode;
        Status = status;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        OutputSummary = outputSummary;
        ErrorMessage = errorMessage;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    private AiTaskRun()
        : base(Guid.NewGuid())
    {
    }

    public string TaskCode { get; private set; } = string.Empty;

    public string Status { get; private set; } = "PENDING";

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string? OutputSummary { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class AiFeedback : AuditableEntity
{
    public AiFeedback(Guid id, Guid promptLogId, Guid userId, int rating, bool wasHelpful, string? correction)
        : base(id)
    {
        PromptLogId = promptLogId;
        UserId = userId;
        Rating = rating;
        WasHelpful = wasHelpful;
        Correction = correction;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    private AiFeedback()
        : base(Guid.NewGuid())
    {
    }

    public Guid PromptLogId { get; private set; }

    public Guid UserId { get; private set; }

    public int Rating { get; private set; }

    public bool WasHelpful { get; private set; }

    public string? Correction { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class AiDocument : AuditableEntity
{
    public AiDocument(
        Guid id,
        string title,
        string sourceType,
        string sourcePath,
        string contentText,
        string languageCode,
        bool isActive,
        Guid createdBy)
        : base(id)
    {
        Title = title;
        SourceType = sourceType;
        SourcePath = sourcePath;
        ContentText = contentText;
        LanguageCode = languageCode;
        IsActive = isActive;
        CreatedBy = createdBy;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    private AiDocument()
        : base(Guid.NewGuid())
    {
    }

    public string Title { get; private set; } = string.Empty;

    public string SourceType { get; private set; } = string.Empty;

    public string SourcePath { get; private set; } = string.Empty;

    public string ContentText { get; private set; } = string.Empty;

    public string LanguageCode { get; private set; } = "ar";

    public bool IsActive { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}

public sealed class AiScheduledTask : AuditableEntity
{
    public AiScheduledTask(Guid id, string taskCode, string cronExpression, bool isEnabled, Guid createdBy)
        : base(id)
    {
        TaskCode = taskCode;
        CronExpression = cronExpression;
        IsEnabled = isEnabled;
        CreatedBy = createdBy;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    private AiScheduledTask()
        : base(Guid.NewGuid())
    {
    }

    public string TaskCode { get; private set; } = string.Empty;

    public string CronExpression { get; private set; } = string.Empty;

    public bool IsEnabled { get; private set; }

    public DateTimeOffset? LastRunAt { get; private set; }

    public DateTimeOffset? NextRunAt { get; private set; }

    public Guid CreatedBy { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Update(string cronExpression, bool isEnabled, Guid by)
    {
        CronExpression = cronExpression;
        IsEnabled = isEnabled;
        UpdatedBy = by;
        Touch();
    }
}

