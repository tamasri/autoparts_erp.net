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

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var response = BuildSafeResponse(message, context);
        stopwatch.Stop();

        await WritePromptLogAsync(sessionId, userId, featureCode, flag.ModelName, message, response, true, (int)stopwatch.ElapsedMilliseconds, cancellationToken);

        return Result<AiChatResult>.Success(new AiChatResult(response, Array.Empty<string>(), flag.ModelName, (int)stopwatch.ElapsedMilliseconds));
    }

    private static string BuildSafeResponse(string message, IReadOnlyDictionary<string, object?>? context)
    {
        if (context is null || context.Count == 0)
        {
            return $"تم استلام طلبك: {message.Trim()}";
        }

        var contextKeys = string.Join(", ", context.Keys.Take(4));
        return $"تم استلام طلبك: {message.Trim()} (السياق: {contextKeys})";
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

