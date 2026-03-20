using AutoPartsERP.Application.Common.Models;

namespace AutoPartsERP.Application.Common.Abstractions;

public interface IGovernanceService
{
    Task<Result<PagedResponse<ApprovalRequestDto>>> GetApprovalsAsync(ApprovalListFilter filter, CancellationToken cancellationToken = default);

    Task<Result<ApprovalRequestDto>> GetApprovalByIdAsync(Guid approvalId, CancellationToken cancellationToken = default);

    Task<Result<ApprovalRequestDto>> CreateApprovalAsync(CreateApprovalRequest request, Guid requesterUserId, CancellationToken cancellationToken = default);

    Task<Result<ApprovalRequestDto>> ApproveApprovalAsync(Guid approvalId, string? comment, Guid reviewerUserId, CancellationToken cancellationToken = default);

    Task<Result<ApprovalRequestDto>> RejectApprovalAsync(Guid approvalId, string comment, Guid reviewerUserId, CancellationToken cancellationToken = default);

    Task<Result<ApprovalRequestDto>> CancelApprovalAsync(Guid approvalId, string reason, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyCollection<PeriodLockDto>>> GetPeriodLocksAsync(PeriodLockFilter filter, CancellationToken cancellationToken = default);

    Task<Result<PeriodLockDto>> LockPeriodAsync(LockPeriodRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<Result<PeriodLockDto>> UnlockPeriodAsync(Guid periodLockId, UnlockPeriodRequest request, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyCollection<ReasonCodeDto>>> GetReasonCodesAsync(ReasonCodeFilter filter, CancellationToken cancellationToken = default);

    Task<Result<ReasonCodeDto>> GetReasonCodeByIdAsync(Guid reasonCodeId, CancellationToken cancellationToken = default);

    Task<Result<ReasonCodeDto>> CreateReasonCodeAsync(CreateReasonCodeRequest request, CancellationToken cancellationToken = default);

    Task<Result<ReasonCodeDto>> UpdateReasonCodeAsync(Guid reasonCodeId, UpdateReasonCodeRequest request, CancellationToken cancellationToken = default);

    Task<Result<PagedResponse<AuditEntryDto>>> SearchAuditAsync(AuditSearchRequest request, CancellationToken cancellationToken = default);

    Task<Result<AuditEntryDto>> GetAuditEntryByIdAsync(Guid auditEntryId, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyCollection<AuditEntryDto>>> GetEntityAuditTrailAsync(string entityType, string entityId, CancellationToken cancellationToken = default);
}
