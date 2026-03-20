using AutoPartsERP.Application.Common.Models;

namespace AutoPartsERP.Application.Common.Abstractions;

public interface IApprovalService
{
    Task<Result<Guid>> CreatePendingApprovalAsync(PendingApprovalSubmission submission, CancellationToken cancellationToken = default);

    Task SaveAsync(ApprovalRequest approvalRequest, CancellationToken cancellationToken = default);
}
