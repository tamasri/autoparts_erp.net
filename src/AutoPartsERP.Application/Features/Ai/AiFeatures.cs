namespace AutoPartsERP.Application.Features.Ai;

public sealed record AiChatCommand(AiChatRequest Request)
    : IRequest<Result<AiChatResponseDto>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Ai.Chat;
    public string AuditModule => "AI";
}

public sealed class AiChatCommandValidator : AbstractValidator<AiChatCommand>
{
    public AiChatCommandValidator()
    {
        RuleFor(x => x.Request.Feature).NotEmpty().MaximumLength(64);
        RuleFor(x => x.Request.Message).NotEmpty().MaximumLength(4000);
    }
}

public sealed class AiChatCommandHandler : IRequestHandler<AiChatCommand, Result<AiChatResponseDto>>
{
    private readonly IAiService _aiService;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public AiChatCommandHandler(IAiService aiService, IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _aiService = aiService;
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<AiChatResponseDto>> Handle(AiChatCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var feature = await connection.QuerySingleOrDefaultAsync<(string FeatureCode, bool IsEnabled, string RequiredPermission)>(
            new CommandDefinition(
                """
                SELECT feature_code AS FeatureCode, is_enabled AS IsEnabled, required_permission AS RequiredPermission
                FROM ai_feature_flags
                WHERE feature_code = @FeatureCode;
                """,
                new { FeatureCode = request.Request.Feature.Trim() },
                cancellationToken: cancellationToken));

        if (string.IsNullOrWhiteSpace(feature.FeatureCode) || !feature.IsEnabled)
        {
            return Result<AiChatResponseDto>.Failure(new Error("Ai.FeatureDisabled", "This AI feature is disabled."));
        }

        if (!_currentUser.HasPermission(feature.RequiredPermission))
        {
            return Result<AiChatResponseDto>.Failure(new Error("Authorization.Forbidden", "You do not have permission for this AI feature."));
        }

        var sessionId = request.Request.SessionId ?? Guid.NewGuid();
        if (request.Request.SessionId is null)
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO ai_sessions (
                    id, user_id, feature_code, title, context_json,
                    is_active, last_interaction_at, expires_at, created_at)
                VALUES (
                    @Id, @UserId, @FeatureCode, @Title, CAST(@ContextJson AS jsonb),
                    TRUE, now(), now() + interval '30 minutes', now());
                """,
                new
                {
                    Id = sessionId,
                    UserId = _currentUser.UserId,
                    FeatureCode = request.Request.Feature.Trim(),
                    Title = $"Session {_currentUser.Username}",
                    ContextJson = JsonSerializer.Serialize(request.Request.Context ?? new Dictionary<string, object?>())
                },
                cancellationToken: cancellationToken));
        }
        else
        {
            await connection.ExecuteAsync(new CommandDefinition(
                """
                UPDATE ai_sessions
                SET last_interaction_at = now(),
                    expires_at = now() + interval '30 minutes'
                WHERE id = @Id;
                """,
                new { Id = sessionId },
                cancellationToken: cancellationToken));
        }

        var chatResult = await _aiService.ChatAsync(
            request.Request.Feature.Trim(),
            request.Request.Message,
            _currentUser.UserId,
            sessionId,
            request.Request.Context,
            cancellationToken);

        if (chatResult.IsFailure || chatResult.Value is null)
        {
            return Result<AiChatResponseDto>.Failure(chatResult.Error);
        }

        return Result<AiChatResponseDto>.Success(new AiChatResponseDto(
            sessionId,
            chatResult.Value.Response,
            chatResult.Value.ToolCallsMade,
            DateTimeOffset.UtcNow));
    }
}

public sealed record GetAiSuggestionsQuery()
    : IRequest<Result<IReadOnlyCollection<AiSuggestionDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Ai.SuggestionsRead;
}

public sealed class GetAiSuggestionsQueryHandler : IRequestHandler<GetAiSuggestionsQuery, Result<IReadOnlyCollection<AiSuggestionDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public GetAiSuggestionsQueryHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyCollection<AiSuggestionDto>>> Handle(GetAiSuggestionsQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<AiSuggestionDto>(
            new CommandDefinition(
                """
                SELECT
                    id AS Id,
                    session_id AS SessionId,
                    feature_code AS FeatureCode,
                    title AS Title,
                    content AS Content,
                    suggested_action_code AS SuggestedActionCode,
                    status AS Status,
                    expires_at AS ExpiresAt,
                    created_at AS CreatedAt
                FROM ai_suggestions
                WHERE user_id = @UserId
                  AND status = 'PENDING'
                ORDER BY expires_at ASC;
                """,
                new { UserId = _currentUser.UserId },
                cancellationToken: cancellationToken));

        return Result<IReadOnlyCollection<AiSuggestionDto>>.Success(rows.ToArray());
    }
}

public sealed record ReviewAiSuggestionCommand(Guid SuggestionId, ReviewAiSuggestionRequest Request)
    : IRequest<Result>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Ai.SuggestionsReview;
    public string AuditModule => "AI";
}

public sealed class ReviewAiSuggestionCommandValidator : AbstractValidator<ReviewAiSuggestionCommand>
{
    public ReviewAiSuggestionCommandValidator()
    {
        RuleFor(x => x.SuggestionId).NotEmpty();
        RuleFor(x => x.Request.Decision).NotEmpty().Must(v => v is "ACCEPTED" or "REJECTED");
    }
}

public sealed class ReviewAiSuggestionCommandHandler : IRequestHandler<ReviewAiSuggestionCommand, Result>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public ReviewAiSuggestionCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(ReviewAiSuggestionCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE ai_suggestions
                SET status = @Decision,
                    reviewed_by = @ReviewedBy,
                    reviewed_at = now(),
                    review_notes = @Notes
                WHERE id = @Id
                  AND status = 'PENDING';
                """,
                new
                {
                    Id = request.SuggestionId,
                    Decision = request.Request.Decision,
                    ReviewedBy = _currentUser.UserId,
                    request.Request.Notes
                },
                cancellationToken: cancellationToken));

        return affected == 0
            ? Result.Failure(new Error("Ai.SuggestionNotFound", "Suggestion not found or already reviewed."))
            : Result.Success();
    }
}

public sealed record SubmitAiFeedbackCommand(SubmitAiFeedbackRequest Request)
    : IRequest<Result>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.Ai.FeedbackCreate;
    public string AuditModule => "AI";
}

public sealed class SubmitAiFeedbackCommandValidator : AbstractValidator<SubmitAiFeedbackCommand>
{
    public SubmitAiFeedbackCommandValidator()
    {
        RuleFor(x => x.Request.PromptLogId).NotEmpty();
        RuleFor(x => x.Request.Rating).InclusiveBetween(1, 5);
    }
}

public sealed class SubmitAiFeedbackCommandHandler : IRequestHandler<SubmitAiFeedbackCommand, Result>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;
    private readonly IKnowledgeBaseService _knowledgeBaseService;

    public SubmitAiFeedbackCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser, IKnowledgeBaseService knowledgeBaseService)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
        _knowledgeBaseService = knowledgeBaseService;
    }

    public async Task<Result> Handle(SubmitAiFeedbackCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO ai_feedback (
                id, prompt_log_id, user_id, rating, was_helpful, correction, created_at)
            VALUES (
                @Id, @PromptLogId, @UserId, @Rating, @WasHelpful, @Correction, now());
            """,
            new
            {
                Id = Guid.NewGuid(),
                request.Request.PromptLogId,
                UserId = _currentUser.UserId,
                request.Request.Rating,
                request.Request.WasHelpful,
                request.Request.Correction
            },
            cancellationToken: cancellationToken));

        if (!string.IsNullOrWhiteSpace(request.Request.Correction))
        {
            var correctionResult = await _knowledgeBaseService.RecordCorrectionAsync(
                request.Request.PromptLogId,
                request.Request.Correction,
                _currentUser.UserId,
                cancellationToken);

            if (correctionResult.IsFailure)
            {
                return Result.Failure(correctionResult.Error);
            }
        }

        return Result.Success();
    }
}

public sealed record IndexKnowledgeDocumentCommand(IndexKnowledgeDocumentRequest Request)
    : IRequest<Result<Guid>>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.System.ConfigWrite;
    public string AuditModule => "AI";
}

public sealed class IndexKnowledgeDocumentCommandHandler : IRequestHandler<IndexKnowledgeDocumentCommand, Result<Guid>>
{
    private readonly IKnowledgeBaseService _knowledgeBaseService;
    private readonly ICurrentUser _currentUser;

    public IndexKnowledgeDocumentCommandHandler(IKnowledgeBaseService knowledgeBaseService, ICurrentUser currentUser)
    {
        _knowledgeBaseService = knowledgeBaseService;
        _currentUser = currentUser;
    }

    public async Task<Result<Guid>> Handle(IndexKnowledgeDocumentCommand request, CancellationToken cancellationToken)
    {
        return await _knowledgeBaseService.IndexDocumentAsync(request.Request, _currentUser.UserId, cancellationToken);
    }
}

public sealed record GetAiSessionsQuery()
    : IRequest<Result<IReadOnlyCollection<AiSessionDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.Ai.Chat;
}

public sealed class GetAiSessionsQueryHandler : IRequestHandler<GetAiSessionsQuery, Result<IReadOnlyCollection<AiSessionDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public GetAiSessionsQueryHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result<IReadOnlyCollection<AiSessionDto>>> Handle(GetAiSessionsQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<AiSessionDto>(
            new CommandDefinition(
                """
                SELECT
                    id AS Id,
                    feature_code AS FeatureCode,
                    title AS Title,
                    is_active AS IsActive,
                    last_interaction_at AS LastInteractionAt,
                    expires_at AS ExpiresAt
                FROM ai_sessions
                WHERE user_id = @UserId
                ORDER BY last_interaction_at DESC;
                """,
                new { UserId = _currentUser.UserId },
                cancellationToken: cancellationToken));

        return Result<IReadOnlyCollection<AiSessionDto>>.Success(rows.ToArray());
    }
}

public sealed record GetAiPromptLogsQuery(int PageNumber = 1, int PageSize = 50)
    : IRequest<Result<PagedResponse<AiPromptLogDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.System.ConfigRead;
}

public sealed class GetAiPromptLogsQueryHandler : IRequestHandler<GetAiPromptLogsQuery, Result<PagedResponse<AiPromptLogDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetAiPromptLogsQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PagedResponse<AiPromptLogDto>>> Handle(GetAiPromptLogsQuery request, CancellationToken cancellationToken)
    {
        var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
        var pageSize = request.PageSize <= 0 ? 50 : Math.Min(request.PageSize, 200);
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var rows = (await connection.QueryAsync<AiPromptLogRow>(
            new CommandDefinition(
                """
                SELECT
                    id AS Id,
                    session_id AS SessionId,
                    feature_code AS FeatureCode,
                    model_name AS ModelName,
                    success AS Success,
                    prompt_tokens AS PromptTokens,
                    completion_tokens AS CompletionTokens,
                    latency_ms AS LatencyMs,
                    created_at AS CreatedAt,
                    COUNT(*) OVER() AS TotalCount
                FROM ai_prompt_logs
                ORDER BY created_at DESC
                OFFSET @Offset
                LIMIT @PageSize;
                """,
                new { Offset = (pageNumber - 1) * pageSize, PageSize = pageSize },
                cancellationToken: cancellationToken))).ToArray();

        var items = rows.Select(x => new AiPromptLogDto(
            x.Id,
            x.SessionId,
            x.FeatureCode,
            x.ModelName,
            x.Success,
            x.PromptTokens,
            x.CompletionTokens,
            x.LatencyMs,
            x.CreatedAt)).ToArray();

        var total = rows.Length == 0 ? 0 : rows[0].TotalCount;
        return Result<PagedResponse<AiPromptLogDto>>.Success(new PagedResponse<AiPromptLogDto>(items, pageNumber, pageSize, total));
    }

    private sealed record AiPromptLogRow(
        Guid Id,
        Guid? SessionId,
        string FeatureCode,
        string ModelName,
        bool Success,
        int? PromptTokens,
        int? CompletionTokens,
        int LatencyMs,
        DateTimeOffset CreatedAt,
        long TotalCount);
}

public sealed record GetAiTaskRunsQuery(int PageNumber = 1, int PageSize = 50)
    : IRequest<Result<PagedResponse<AiTaskRunDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.System.ConfigRead;
}

public sealed class GetAiTaskRunsQueryHandler : IRequestHandler<GetAiTaskRunsQuery, Result<PagedResponse<AiTaskRunDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetAiTaskRunsQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<PagedResponse<AiTaskRunDto>>> Handle(GetAiTaskRunsQuery request, CancellationToken cancellationToken)
    {
        var pageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
        var pageSize = request.PageSize <= 0 ? 50 : Math.Min(request.PageSize, 200);
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);

        var rows = (await connection.QueryAsync<AiTaskRunRow>(
            new CommandDefinition(
                """
                SELECT
                    id AS Id,
                    task_code AS TaskCode,
                    status AS Status,
                    started_at AS StartedAt,
                    completed_at AS CompletedAt,
                    output_summary AS OutputSummary,
                    error_message AS ErrorMessage,
                    COUNT(*) OVER() AS TotalCount
                FROM ai_task_runs
                ORDER BY started_at DESC
                OFFSET @Offset
                LIMIT @PageSize;
                """,
                new { Offset = (pageNumber - 1) * pageSize, PageSize = pageSize },
                cancellationToken: cancellationToken))).ToArray();

        var items = rows.Select(x => new AiTaskRunDto(
            x.Id,
            x.TaskCode,
            x.Status,
            x.StartedAt,
            x.CompletedAt,
            x.OutputSummary,
            x.ErrorMessage)).ToArray();

        var total = rows.Length == 0 ? 0 : rows[0].TotalCount;
        return Result<PagedResponse<AiTaskRunDto>>.Success(new PagedResponse<AiTaskRunDto>(items, pageNumber, pageSize, total));
    }

    private sealed record AiTaskRunRow(
        Guid Id,
        string TaskCode,
        string Status,
        DateTimeOffset StartedAt,
        DateTimeOffset? CompletedAt,
        string? OutputSummary,
        string? ErrorMessage,
        long TotalCount);
}

public sealed record GetAiFeatureFlagsQuery()
    : IRequest<Result<IReadOnlyCollection<AiFeatureFlagDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.System.ConfigRead;
}

public sealed class GetAiFeatureFlagsQueryHandler : IRequestHandler<GetAiFeatureFlagsQuery, Result<IReadOnlyCollection<AiFeatureFlagDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetAiFeatureFlagsQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<IReadOnlyCollection<AiFeatureFlagDto>>> Handle(GetAiFeatureFlagsQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<AiFeatureFlagDto>(
            new CommandDefinition(
                """
                SELECT
                    id AS Id,
                    feature_code AS FeatureCode,
                    label AS Label,
                    model_name AS ModelName,
                    is_enabled AS IsEnabled,
                    required_permission AS RequiredPermission,
                    allowed_roles AS AllowedRoles
                FROM ai_feature_flags
                ORDER BY feature_code ASC;
                """,
                cancellationToken: cancellationToken));

        return Result<IReadOnlyCollection<AiFeatureFlagDto>>.Success(rows.ToArray());
    }
}

public sealed record UpdateAiFeatureFlagCommand(string FeatureCode, UpdateAiFeatureFlagRequest Request)
    : IRequest<Result>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.System.ConfigWrite;
    public string AuditModule => "AI";
}

public sealed class UpdateAiFeatureFlagCommandHandler : IRequestHandler<UpdateAiFeatureFlagCommand, Result>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public UpdateAiFeatureFlagCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(UpdateAiFeatureFlagCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE ai_feature_flags
                SET is_enabled = @IsEnabled,
                    model_name = @ModelName,
                    required_permission = @RequiredPermission,
                    allowed_roles = @AllowedRoles,
                    updated_at = now(),
                    updated_by = @UpdatedBy
                WHERE feature_code = @FeatureCode;
                """,
                new
                {
                    request.Request.IsEnabled,
                    request.Request.ModelName,
                    request.Request.RequiredPermission,
                    AllowedRoles = request.Request.AllowedRoles.ToArray(),
                    UpdatedBy = _currentUser.UserId,
                    FeatureCode = request.FeatureCode
                },
                cancellationToken: cancellationToken));

        return affected == 0
            ? Result.Failure(new Error("Ai.FeatureNotFound", "AI feature flag was not found."))
            : Result.Success();
    }
}

public sealed record GetAiScheduledTasksQuery()
    : IRequest<Result<IReadOnlyCollection<AiScheduledTaskDto>>>, IAuthorizedRequest
{
    public string RequiredPermission => PermissionCodes.System.ConfigRead;
}

public sealed class GetAiScheduledTasksQueryHandler : IRequestHandler<GetAiScheduledTasksQuery, Result<IReadOnlyCollection<AiScheduledTaskDto>>>
{
    private readonly IDbConnectionFactory _connectionFactory;

    public GetAiScheduledTasksQueryHandler(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<Result<IReadOnlyCollection<AiScheduledTaskDto>>> Handle(GetAiScheduledTasksQuery request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var rows = await connection.QueryAsync<AiScheduledTaskDto>(
            new CommandDefinition(
                """
                SELECT
                    id AS Id,
                    task_code AS TaskCode,
                    cron_expression AS CronExpression,
                    is_enabled AS IsEnabled,
                    last_run_at AS LastRunAt,
                    next_run_at AS NextRunAt
                FROM ai_scheduled_tasks
                ORDER BY task_code ASC;
                """,
                cancellationToken: cancellationToken));

        return Result<IReadOnlyCollection<AiScheduledTaskDto>>.Success(rows.ToArray());
    }
}

public sealed record UpdateAiScheduledTaskCommand(Guid TaskId, UpdateAiScheduledTaskRequest Request)
    : IRequest<Result>, IAuthorizedRequest, IAuditableRequest
{
    public string RequiredPermission => PermissionCodes.System.ConfigWrite;
    public string AuditModule => "AI";
}

public sealed class UpdateAiScheduledTaskCommandHandler : IRequestHandler<UpdateAiScheduledTaskCommand, Result>
{
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ICurrentUser _currentUser;

    public UpdateAiScheduledTaskCommandHandler(IDbConnectionFactory connectionFactory, ICurrentUser currentUser)
    {
        _connectionFactory = connectionFactory;
        _currentUser = currentUser;
    }

    public async Task<Result> Handle(UpdateAiScheduledTaskCommand request, CancellationToken cancellationToken)
    {
        await using var connection = await _connectionFactory.CreateOpenConnectionAsync(cancellationToken);
        var affected = await connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE ai_scheduled_tasks
                SET cron_expression = @CronExpression,
                    is_enabled = @IsEnabled,
                    updated_at = now(),
                    updated_by = @UpdatedBy
                WHERE id = @Id;
                """,
                new
                {
                    Id = request.TaskId,
                    request.Request.CronExpression,
                    request.Request.IsEnabled,
                    UpdatedBy = _currentUser.UserId
                },
                cancellationToken: cancellationToken));

        return affected == 0
            ? Result.Failure(new Error("Ai.ScheduledTaskNotFound", "Scheduled task was not found."))
            : Result.Success();
    }
}

