using AutoPartsERP.Application.Common.Abstractions.Repositories;

namespace AutoPartsERP.Infrastructure.Persistence.Repositories;

public sealed class AiRepository : IAiRepository
{
    private readonly AppDbContext _dbContext;

    public AiRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<AiFeatureFlag?> GetFeatureFlagAsync(string featureCode, CancellationToken cancellationToken = default)
    {
        return await _dbContext.AiFeatureFlags.FirstOrDefaultAsync(
            x => x.FeatureCode == featureCode,
            cancellationToken);
    }

    public async Task<AiSession?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.AiSessions.FirstOrDefaultAsync(x => x.Id == sessionId, cancellationToken);
    }

    public async Task AddSessionAsync(AiSession session, CancellationToken cancellationToken = default)
    {
        await _dbContext.AiSessions.AddAsync(session, cancellationToken);
    }

    public async Task AddPromptLogAsync(AiPromptLog promptLog, CancellationToken cancellationToken = default)
    {
        await _dbContext.AiPromptLogs.AddAsync(promptLog, cancellationToken);
    }

    public async Task<IReadOnlyCollection<AiSuggestion>> GetPendingSuggestionsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.AiSuggestions
            .Where(x => x.UserId == userId && x.Status == "PENDING")
            .OrderBy(x => x.ExpiresAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<AiSuggestion?> GetSuggestionAsync(Guid suggestionId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.AiSuggestions.FirstOrDefaultAsync(x => x.Id == suggestionId, cancellationToken);
    }

    public async Task AddFeedbackAsync(AiFeedback feedback, CancellationToken cancellationToken = default)
    {
        await _dbContext.AiFeedback.AddAsync(feedback, cancellationToken);
    }

    public async Task AddTaskRunAsync(AiTaskRun taskRun, CancellationToken cancellationToken = default)
    {
        await _dbContext.AiTaskRuns.AddAsync(taskRun, cancellationToken);
    }
}

