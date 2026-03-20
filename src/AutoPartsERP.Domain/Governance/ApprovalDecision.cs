using AutoPartsERP.Domain.Common;
using AutoPartsERP.Domain.Constants;

namespace AutoPartsERP.Domain.Governance;

public sealed class ApprovalDecision : AuditableEntity
{
    public ApprovalDecision(Guid id, Guid approvalRequestId, Guid reviewerUserId, string status, string? comment = null)
        : base(id)
    {
        ApprovalRequestId = approvalRequestId;
        ReviewerUserId = reviewerUserId;
        Status = status;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim();
        ReviewedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid ApprovalRequestId { get; }

    public Guid ReviewerUserId { get; }

    public string Status { get; }

    public string? Comment { get; }

    public DateTimeOffset ReviewedAtUtc { get; }

    public bool IsApproval => string.Equals(Status, ApprovalStatuses.Approved, StringComparison.OrdinalIgnoreCase);

    public bool IsRejection => string.Equals(Status, ApprovalStatuses.Rejected, StringComparison.OrdinalIgnoreCase);
}
