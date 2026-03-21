namespace AutoPartsERP.Application.Common.Abstractions.Repositories;

public interface IAiRepository
{
    Task<AiFeatureFlag?> GetFeatureFlagAsync(string featureCode, CancellationToken cancellationToken = default);
    Task<AiSession?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task AddSessionAsync(AiSession session, CancellationToken cancellationToken = default);
    Task AddPromptLogAsync(AiPromptLog promptLog, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<AiSuggestion>> GetPendingSuggestionsAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<AiSuggestion?> GetSuggestionAsync(Guid suggestionId, CancellationToken cancellationToken = default);
    Task AddFeedbackAsync(AiFeedback feedback, CancellationToken cancellationToken = default);
    Task AddTaskRunAsync(AiTaskRun taskRun, CancellationToken cancellationToken = default);
}

