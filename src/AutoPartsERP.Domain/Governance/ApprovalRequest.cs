using AutoPartsERP.Domain.Common;
using AutoPartsERP.Domain.Constants;

namespace AutoPartsERP.Domain.Governance;

public sealed class ApprovalRequest : AuditableEntity
{
    private readonly List<ApprovalDecision> _decisions = new();

    private ApprovalRequest()
        : base(Guid.Empty)
    {
    }

    public ApprovalRequest(
        Guid id,
        string entityType,
        string entityId,
        string actionCode,
        Guid requestedByUserId,
        string reason,
        int requiredApprovals = 1)
        : base(id)
    {
        EntityType = entityType.Trim();
        EntityId = entityId.Trim();
        ActionCode = actionCode.Trim();
        RequestedByUserId = requestedByUserId;
        Reason = reason.Trim();
        RequiredApprovals = requiredApprovals < 1 ? 1 : requiredApprovals;
        Status = ApprovalStatuses.Pending;
        RequestedAtUtc = DateTimeOffset.UtcNow;
    }

    public string EntityType { get; private set; } = string.Empty;

    public string EntityId { get; private set; } = string.Empty;

    public string ActionCode { get; private set; } = string.Empty;

    public Guid RequestedByUserId { get; private set; }

    public string Reason { get; private set; } = null!;

    public string Status { get; private set; } = null!;

    public int RequiredApprovals { get; private set; }

    public int CurrentApprovals => _decisions.Count(decision => decision.IsApproval);

    public DateTimeOffset RequestedAtUtc { get; private set; }

    public DateTimeOffset? CompletedAtUtc { get; private set; }

    public IReadOnlyCollection<ApprovalDecision> Decisions => _decisions.AsReadOnly();

    public Result Approve(Guid reviewerUserId, string? comment = null)
    {
        return Review(reviewerUserId, ApprovalStatuses.Approved, comment);
    }

    public Result Reject(Guid reviewerUserId, string comment)
    {
        return Review(reviewerUserId, ApprovalStatuses.Rejected, comment);
    }

    public Result Cancel(string reason)
    {
        if (ApprovalStatuses.TerminalStatuses.Contains(Status, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Failure(new Error("approval.already-completed", "The approval request has already been completed."));
        }

        Reason = reason.Trim();
        Status = ApprovalStatuses.Cancelled;
        CompletedAtUtc = DateTimeOffset.UtcNow;
        Touch();
        return Result.Success();
    }

    private Result Review(Guid reviewerUserId, string decisionStatus, string? comment)
    {
        if (ApprovalStatuses.TerminalStatuses.Contains(Status, StringComparer.OrdinalIgnoreCase))
        {
            return Result.Failure(new Error("approval.already-completed", "The approval request has already been completed."));
        }

        if (_decisions.Any(decision => decision.ReviewerUserId == reviewerUserId))
        {
            return Result.Failure(new Error("approval.duplicate-review", "The reviewer has already submitted a decision for this request."));
        }

        var decision = new ApprovalDecision(Guid.NewGuid(), Id, reviewerUserId, decisionStatus, comment);
        _decisions.Add(decision);

        if (decision.IsRejection)
        {
            Status = ApprovalStatuses.Rejected;
            CompletedAtUtc = DateTimeOffset.UtcNow;
        }
        else if (CurrentApprovals >= RequiredApprovals)
        {
            Status = ApprovalStatuses.Approved;
            CompletedAtUtc = DateTimeOffset.UtcNow;
        }
        else
        {
            Status = ApprovalStatuses.InReview;
        }

        Touch();
        return Result.Success();
    }
}
