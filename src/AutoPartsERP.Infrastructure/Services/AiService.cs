using AutoPartsERP.Application.Common.Abstractions;
using AutoPartsERP.Application.Common.Abstractions.Repositories;
using AutoPartsERP.Domain.Ai;

namespace AutoPartsERP.Infrastructure.Services;

public sealed class AiService : IAiService
{
    private readonly IAiRepository _aiRepository;
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUser _currentUser;
    private readonly ILogger<AiService> _logger;

    public AiService(
        IAiRepository aiRepository,
        AppDbContext dbContext,
        ICurrentUser currentUser,
        ILogger<AiService> logger)
    {
        _aiRepository = aiRepository;
        _dbContext = dbContext;
        _currentUser = currentUser;
        _logger = logger;
    }

    public async Task<Result<AiChatResult>> ChatAsync(
        string featureCode,
        string message,
        Guid userId,
        Guid? sessionId,
        IReadOnlyDictionary<string, object?>? context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(featureCode))
        {
            return Result<AiChatResult>.Failure(new Error("Ai.FeatureRequired", "Feature code is required."));
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return Result<AiChatResult>.Failure(new Error("Ai.MessageRequired", "Message is required."));
        }

        var flag = await _aiRepository.GetFeatureFlagAsync(featureCode.Trim(), cancellationToken);
        if (flag is null || !flag.IsEnabled)
        {
            await WritePromptLogAsync(sessionId, userId, featureCode, flag?.ModelName ?? "n/a", message, "Feature disabled", false, 0, cancellationToken);
            return Result<AiChatResult>.Failure(new Error("Ai.FeatureDisabled", "This AI feature is disabled."));
        }

        if (!_currentUser.HasPermission(flag.RequiredPermission))
        {
            await WritePromptLogAsync(sessionId, userId, featureCode, flag.ModelName, message, "Permission denied", false, 0, cancellationToken);
            return Result<AiChatResult>.Failure(new Error("Authorization.Forbidden", "You do not have permission to use this AI feature."));
        }

        if (flag.AllowWritesToCoreData)
        {
            _logger.LogWarning("AI feature {FeatureCode} is configured with write permissions. Blocking request by policy.", featureCode);
            await WritePromptLogAsync(sessionId, userId, featureCode, flag.ModelName, message, "Policy blocked", false, 0, cancellationToken);
            return Result<AiChatResult>.Failure(new Error("Ai.PolicyViolation", "AI core-data writes are forbidden by policy."));
        }

        // No language-model provider is wired yet (Phase 6: an OpenAI-compatible provider such as Groq/DeepSeek).
        // Say so plainly. This method used to echo the user's own message back as if it were an answer, which
        // made a stub look like a working assistant.
        await WritePromptLogAsync(sessionId, userId, featureCode, flag.ModelName, message, "Provider not configured", false, 0, cancellationToken);
        return Result<AiChatResult>.Failure(new Error(
            "Ai.ProviderNotConfigured",
            "The AI assistant is not available yet: no language-model provider is configured."));
    }

    private async Task WritePromptLogAsync(
        Guid? sessionId,
        Guid userId,
        string featureCode,
        string modelName,
        string prompt,
        string response,
        bool success,
        int latencyMs,
        CancellationToken cancellationToken)
    {
        var log = new AiPromptLog(
            Guid.NewGuid(),
            sessionId,
            userId,
            featureCode,
            modelName,
            prompt,
            response,
            success,
            null,
            null,
            latencyMs,
            null);

        await _aiRepository.AddPromptLogAsync(log, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

