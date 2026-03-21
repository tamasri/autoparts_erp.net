using AutoPartsERP.Application.Common.Abstractions;
using AutoPartsERP.Domain.Ai;

namespace AutoPartsERP.Infrastructure.Services;

public sealed class KnowledgeBaseService : IKnowledgeBaseService
{
    private readonly AppDbContext _dbContext;

    public KnowledgeBaseService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<Guid>> IndexDocumentAsync(
        IndexKnowledgeDocumentRequest request,
        Guid createdBy,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title) ||
            string.IsNullOrWhiteSpace(request.SourcePath) ||
            string.IsNullOrWhiteSpace(request.ContentText))
        {
            return Result<Guid>.Failure(new Error("Ai.DocumentInvalid", "Document title, source path, and content are required."));
        }

        var existing = await _dbContext.AiDocuments.FirstOrDefaultAsync(x => x.SourcePath == request.SourcePath, cancellationToken);
        if (existing is not null)
        {
            _dbContext.Remove(existing);
        }

        var document = new AiDocument(
            Guid.NewGuid(),
            request.Title.Trim(),
            "FILE",
            request.SourcePath.Trim(),
            request.ContentText.Trim(),
            string.IsNullOrWhiteSpace(request.LanguageCode) ? "ar" : request.LanguageCode.Trim(),
            true,
            createdBy);

        await _dbContext.AiDocuments.AddAsync(document, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(document.Id);
    }

    public async Task<Result<Guid>> RecordCorrectionAsync(
        Guid promptLogId,
        string correction,
        Guid createdBy,
        CancellationToken cancellationToken = default)
    {
        if (promptLogId == Guid.Empty || string.IsNullOrWhiteSpace(correction))
        {
            return Result<Guid>.Failure(new Error("Ai.CorrectionInvalid", "Prompt log id and correction are required."));
        }

        var promptLog = await _dbContext.AiPromptLogs.FirstOrDefaultAsync(x => x.Id == promptLogId, cancellationToken);
        if (promptLog is null)
        {
            return Result<Guid>.Failure(new Error("Ai.PromptLogNotFound", "Prompt log was not found."));
        }

        var document = new AiDocument(
            Guid.NewGuid(),
            $"Correction {DateTimeOffset.UtcNow:yyyyMMddHHmmss}",
            "SYSTEM_LEARNED",
            $"prompt-log:{promptLogId}",
            correction.Trim(),
            "ar",
            true,
            createdBy);

        await _dbContext.AiDocuments.AddAsync(document, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result<Guid>.Success(document.Id);
    }
}

