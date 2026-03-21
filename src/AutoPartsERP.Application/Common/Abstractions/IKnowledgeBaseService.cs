namespace AutoPartsERP.Application.Common.Abstractions;

public interface IKnowledgeBaseService
{
    Task<Result<Guid>> IndexDocumentAsync(
        IndexKnowledgeDocumentRequest request,
        Guid createdBy,
        CancellationToken cancellationToken = default);

    Task<Result<Guid>> RecordCorrectionAsync(
        Guid promptLogId,
        string correction,
        Guid createdBy,
        CancellationToken cancellationToken = default);
}

