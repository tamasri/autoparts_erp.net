using AutoPartsERP.Application.Common.Models;

namespace AutoPartsERP.Application.Common.Abstractions;

public interface IApprovalService
{
    Task<Result<Guid>> CreatePendingApprovalAsync(PendingApprovalSubmission submission, CancellationToken cancellationToken = default);

    /// <summary>Whether a request of this type for this document is already waiting for approval.</summary>
    Task<bool> HasPendingAsync(string requestType, string entityId, CancellationToken cancellationToken = default);

    Task SaveAsync(ApprovalRequest approvalRequest, CancellationToken cancellationToken = default);
}
