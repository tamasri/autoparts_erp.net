using AutoPartsERP.Domain.Common;
using AutoPartsERP.Domain.Constants;

namespace AutoPartsERP.Domain.Governance;

public sealed class ApprovalDecision : AuditableEntity
{
    private ApprovalDecision()
        : base(Guid.Empty)
    {
    }

    public ApprovalDecision(Guid id, Guid approvalRequestId, Guid reviewerUserId, string status, string? comment = null)
        : base(id)
    {
        ApprovalRequestId = approvalRequestId;
        ReviewerUserId = reviewerUserId;
        Status = status;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        ReviewedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid ApprovalRequestId { get; private set; }

    public Guid ReviewerUserId { get; private set; }

    public string Status { get; private set; } = string.Empty;

    public string? Comment { get; private set; }

    public DateTimeOffset ReviewedAtUtc { get; private set; }

    public bool IsApproval => string.Equals(Status, ApprovalStatuses.Approved, StringComparison.OrdinalIgnoreCase);

    public bool IsRejection => string.Equals(Status, ApprovalStatuses.Rejected, StringComparison.OrdinalIgnoreCase);
}
