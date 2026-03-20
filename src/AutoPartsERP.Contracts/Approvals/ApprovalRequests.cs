namespace AutoPartsERP.Contracts.Approvals;

public sealed record CreateApprovalRequest(
    string EntityType,
    string EntityId,
    string ActionCode,
    string Reason,
    int RequiredApprovals = 1);

public sealed record ApproveApprovalRequest(string? Comment = null);

public sealed record RejectApprovalRequest(string Comment);

public sealed record CancelApprovalRequest(string Reason);
